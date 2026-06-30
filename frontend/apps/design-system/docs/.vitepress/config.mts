import { fileURLToPath, URL } from 'node:url'
import tailwindcss from '@tailwindcss/vite'
import { defineConfig } from 'vitepress'
import wasm from 'vite-plugin-wasm'

// Nerv-IIP 设计系统文档 (VitePress).
// Runs under the workspace's `vite → @voidzero-dev/vite-plus-core` override; the
// docs app carries an `esbuild` devDep because Rolldown-Vite deprecated
// `transformWithEsbuild` (still called by @vitejs/plugin-vue).
export default defineConfig({
  title: 'Nerv-IIP 设计系统',
  description: '数字工厂控制平面 · 统一设计系统：设计哲学、设计令牌与组件展示',
  lang: 'zh-CN',
  cleanUrls: true,
  appearance: 'dark', // 默认黑主题，与设计方向一致；导航栏仍可切换亮/暗
  lastUpdated: true,

  themeConfig: {
    logo: undefined,
    // 表面（桌面 / PDA / 看板）作为顶栏一级入口；各表面内部按分类在侧栏列出组件。
    nav: [
      { text: '指南', link: '/guide/introduction', activeMatch: '/guide/' },
      {
        text: '基础',
        activeMatch: '/foundations/',
        items: [
          { text: '设计哲学', link: '/foundations/philosophy' },
          { text: '设计令牌', link: '/foundations/tokens' },
          { text: '色彩与动态色', link: '/foundations/color' },
          { text: '动效', link: '/foundations/motion' },
        ],
      },
      { text: '桌面 PC', link: '/components/desktop/', activeMatch: '/components/desktop' },
      { text: 'PDA 移动', link: '/components/mobile/', activeMatch: '/components/mobile' },
      { text: '一体机看板', link: '/components/board', activeMatch: '/components/board' },
      { text: '大屏', link: '/components/screen/', activeMatch: '/components/screen' },
    ],
    sidebar: {
      '/guide/': [
        {
          text: '开始',
          items: [
            { text: '介绍', link: '/guide/introduction' },
            { text: '三个表面', link: '/guide/surfaces' },
            { text: '组件概览', link: '/components/overview' },
          ],
        },
      ],
      '/foundations/': [
        {
          text: '基础',
          items: [
            { text: '设计哲学', link: '/foundations/philosophy' },
            { text: '设计令牌', link: '/foundations/tokens' },
            { text: '色彩与动态色', link: '/foundations/color' },
            { text: '动效', link: '/foundations/motion' },
          ],
        },
      ],
      // 桌面 PC —— 组件库式：每个组件一页，按分类分组
      '/components/desktop': [
        {
          text: '桌面 PC',
          items: [
            { text: '概览', link: '/components/desktop/' },
            { text: '完整示例', link: '/components/desktop/gallery' },
          ],
        },
        {
          text: '通用',
          items: [
            { text: 'Button 按钮', link: '/components/desktop/button' },
            { text: 'Badge 徽标', link: '/components/desktop/badge' },
            { text: 'Card 卡片', link: '/components/desktop/card' },
          ],
        },
        {
          text: '布局',
          items: [
            { text: 'Container 容器', link: '/components/desktop/container' },
            { text: 'Page 页面布局', link: '/components/desktop/page' },
            { text: 'PageGrid 卡片网格', link: '/components/desktop/page-grid' },
            { text: 'PageColumns 瀑布分栏', link: '/components/desktop/page-columns' },
            { text: 'PageSection 内容区块', link: '/components/desktop/page-section' },
          ],
        },
        {
          text: '导航 / 外壳',
          items: [
            { text: 'App & Header 应用外壳', link: '/components/desktop/app' },
            { text: 'NavigationMenu 导航菜单', link: '/components/desktop/navigation-menu' },
            { text: 'Breadcrumb 面包屑', link: '/components/desktop/breadcrumb' },
          ],
        },
        {
          text: '仪表盘 / 外壳',
          items: [
            { text: 'Sidebar 侧栏', link: '/components/desktop/sidebar' },
            { text: 'AppShellInset 应用外壳', link: '/components/desktop/dashboard' },
          ],
        },
        {
          text: '表单',
          items: [
            { text: 'Input 输入框', link: '/components/desktop/input' },
            { text: 'Field 表单字段', link: '/components/desktop/field' },
            { text: 'Select 选择器', link: '/components/desktop/select' },
            { text: 'Checkbox 复选框', link: '/components/desktop/checkbox' },
            { text: 'Radio 单选框', link: '/components/desktop/radio' },
            { text: 'Switch 开关', link: '/components/desktop/switch' },
            { text: 'Slider 滑块', link: '/components/desktop/slider' },
            { text: 'DatePicker 日期选择器', link: '/components/desktop/date-picker' },
            { text: 'TimePicker 时间选择器', link: '/components/desktop/time-picker' },
          ],
        },
        {
          text: '数据展示',
          items: [
            { text: 'DataTable 数据表格', link: '/components/desktop/data-table' },
            { text: 'Descriptions 描述列表', link: '/components/desktop/descriptions' },
            { text: 'Timeline 时间线', link: '/components/desktop/timeline' },
            { text: 'Tabs 标签页', link: '/components/desktop/tabs' },
            { text: 'Carousel 轮播图', link: '/components/desktop/carousel' },
            { text: 'FilePreview 文件预览', link: '/components/desktop/file-preview' },
            { text: 'Status 状态', link: '/components/desktop/status' },
          ],
        },
        {
          text: '反馈',
          items: [
            { text: 'Alert 警告提示', link: '/components/desktop/alert' },
            { text: 'Dialog 对话框', link: '/components/desktop/dialog' },
            { text: 'AlertDialog 警示对话框', link: '/components/desktop/alert-dialog' },
            { text: 'Sheet 抽屉', link: '/components/desktop/sheet' },
            { text: 'DropdownMenu 下拉菜单', link: '/components/desktop/dropdown-menu' },
            { text: 'Popconfirm 气泡确认', link: '/components/desktop/popconfirm' },
            { text: 'Tooltip 文字提示', link: '/components/desktop/tooltip' },
            { text: 'Notify 消息提醒', link: '/components/desktop/notify' },
            { text: 'Loader 加载', link: '/components/desktop/loader' },
            { text: 'Command 命令面板', link: '/components/desktop/command' },
          ],
        },
        {
          text: '图表',
          items: [{ text: 'Chart 图表', link: '/components/desktop/chart' }],
        },
      ],
      // PDA 移动 —— 组件库式：每个组件一页，按分类分组
      '/components/mobile': [
        {
          text: 'PDA 移动',
          items: [
            { text: '概览', link: '/components/mobile/' },
            { text: '完整示例', link: '/components/mobile/gallery' },
            { text: 'AppShellMobile 应用外壳', link: '/components/mobile/app-shell-mobile' },
          ],
        },
        {
          text: '基础',
          items: [
            { text: 'Button 按钮', link: '/components/mobile/button' },
            { text: 'Badge 角标', link: '/components/mobile/badge' },
            { text: 'Cell 单元格', link: '/components/mobile/cell' },
            { text: 'ListRow 列表行', link: '/components/mobile/list-row' },
            { text: 'Divider 分割线', link: '/components/mobile/divider' },
          ],
        },
        {
          text: '数据展示',
          items: [
            { text: 'Avatar 头像', link: '/components/mobile/avatar' },
            { text: 'Tag 标签', link: '/components/mobile/tag' },
            { text: 'Skeleton 骨架屏', link: '/components/mobile/skeleton' },
          ],
        },
        {
          text: '表单',
          items: [
            { text: 'Input 输入框', link: '/components/mobile/input' },
            { text: 'Checkbox 复选框', link: '/components/mobile/checkbox' },
            { text: 'Radio 单选框', link: '/components/mobile/radio' },
            { text: 'Switch 开关', link: '/components/mobile/switch' },
            { text: 'Stepper 步进器', link: '/components/mobile/stepper' },
            { text: 'Slider 滑块', link: '/components/mobile/slider' },
            { text: 'Rate 评分', link: '/components/mobile/rate' },
            { text: 'NumberKeyboard 数字键盘', link: '/components/mobile/number-keyboard' },
            { text: 'SearchBar 搜索栏', link: '/components/mobile/search-bar' },
            { text: 'Picker 滚轮选择', link: '/components/mobile/picker' },
            { text: 'DatePicker 日期选择', link: '/components/mobile/date-picker' },
            { text: 'ScanBar 扫码栏', link: '/components/mobile/scan-bar' },
          ],
        },
        {
          text: '导航',
          items: [
            { text: 'TabBar 标签栏', link: '/components/mobile/tab-bar' },
            { text: 'Tabs 顶部标签', link: '/components/mobile/tabs' },
            { text: 'Steps 步骤条', link: '/components/mobile/steps' },
            { text: 'NavBar 顶部栏', link: '/components/mobile/nav-bar' },
            { text: 'Collapse 折叠面板', link: '/components/mobile/collapse' },
            { text: 'DropdownMenu 下拉筛选', link: '/components/mobile/dropdown-menu' },
          ],
        },
        {
          text: '数据展示',
          items: [
            { text: 'Swiper 轮播图', link: '/components/mobile/swiper' },
            { text: 'Image 图片', link: '/components/mobile/image' },
          ],
        },
        {
          text: '反馈',
          items: [
            { text: 'ActionSheet 动作面板', link: '/components/mobile/action-sheet' },
            { text: 'BottomSheet 底部抽屉', link: '/components/mobile/bottom-sheet' },
            { text: 'Dialog 对话框', link: '/components/mobile/dialog' },
            { text: 'Toast 居中提示', link: '/components/mobile/toast' },
            { text: 'NoticeBar 通知条', link: '/components/mobile/notice-bar' },
            { text: 'Progress 进度条', link: '/components/mobile/progress' },
            { text: 'Result 结果页', link: '/components/mobile/result' },
            { text: 'Empty 空状态', link: '/components/mobile/empty' },
          ],
        },
        {
          text: '手势 / 操作',
          items: [
            { text: 'Fab 悬浮按钮', link: '/components/mobile/fab' },
            { text: 'Grid 宫格', link: '/components/mobile/grid' },
            { text: 'PullRefresh 下拉刷新', link: '/components/mobile/pull-refresh' },
            { text: 'SwipeCell 侧滑操作', link: '/components/mobile/swipe-cell' },
          ],
        },
        {
          text: '长列表',
          items: [
            { text: 'VirtualList 虚拟列表', link: '/components/mobile/virtual-list' },
            { text: 'InfiniteList 无限滚动', link: '/components/mobile/infinite-list' },
          ],
        },
      ],
      '/components/board': [
        {
          text: '一体机看板',
          items: [{ text: '工位看板', link: '/components/board' }],
        },
      ],
      // 大屏 / 控制室 —— 独立 --sb-* 工业蓝令牌层，每组件一页，按分类分组
      '/components/screen': [
        {
          text: '大屏 / 控制室',
          items: [{ text: '概览', link: '/components/screen/' }],
        },
        {
          text: '容器 / 外壳',
          items: [
            { text: 'ScreenPanel 面板', link: '/components/screen/screen-panel' },
            { text: 'BorderPanel 描边面板', link: '/components/screen/border-panel' },
            { text: 'TechFrame 科技边框', link: '/components/screen/tech-frame' },
            { text: 'TitleBar 标题栏', link: '/components/screen/title-bar' },
            { text: 'ScreenHeader 大屏页头', link: '/components/screen/screen-header' },
            { text: 'GlowDivider 辉光分割', link: '/components/screen/glow-divider' },
          ],
        },
        {
          text: '指标 / 图表',
          items: [
            { text: 'OeeHero 核心指标', link: '/components/screen/oee-hero' },
            { text: 'RingGauge 环形仪表', link: '/components/screen/ring-gauge' },
            { text: 'CapsuleBar 胶囊进度', link: '/components/screen/capsule-bar' },
            { text: 'DigitalFlop 数字翻牌', link: '/components/screen/digital-flop' },
            { text: 'Sparkline 迷你趋势', link: '/components/screen/sparkline' },
            { text: 'TrendChart 趋势图', link: '/components/screen/trend-chart' },
            { text: 'TaktGantt 节拍甘特', link: '/components/screen/takt-gantt' },
          ],
        },
        {
          text: '数据 / 状态',
          items: [
            { text: 'StatusCard 产线状态卡', link: '/components/screen/status-card' },
            { text: 'KpiBar 指标条', link: '/components/screen/kpi-bar' },
            { text: 'AlarmTable 告警表', link: '/components/screen/alarm-table' },
            { text: 'StatusLight 状态灯', link: '/components/screen/status-light' },
            { text: 'StatusTag 状态标签', link: '/components/screen/status-tag' },
          ],
        },
        {
          text: '控件（大屏化）',
          items: [
            { text: 'ScreenButton 按钮', link: '/components/screen/screen-button' },
            { text: 'ScreenInput 输入框', link: '/components/screen/screen-input' },
            { text: 'ScreenSelect 下拉选择', link: '/components/screen/screen-select' },
            { text: 'ScreenSearch 搜索框', link: '/components/screen/screen-search' },
            { text: 'ScreenTable 数据表格', link: '/components/screen/screen-table' },
            { text: 'ScreenTabs 标签页', link: '/components/screen/screen-tabs' },
            { text: 'ScreenSegmented 分段控制', link: '/components/screen/screen-segmented' },
            { text: 'ScreenSwitch 开关', link: '/components/screen/screen-switch' },
            { text: 'ScreenPagination 分页', link: '/components/screen/screen-pagination' },
          ],
        },
      ],
    },
    socialLinks: [{ icon: 'github', link: 'https://github.com/Mang-X/Nerv-IIP' }],
    search: { provider: 'local' },
    outline: { level: [2, 3], label: '本页目录' },
    docFooter: { prev: '上一页', next: '下一页' },
    darkModeSwitchLabel: '外观',
    returnToTopLabel: '返回顶部',
    sidebarMenuLabel: '菜单',
    lastUpdatedText: '最后更新',
  },

  vite: {
    plugins: [wasm(), tailwindcss()],
    resolve: {
      alias: {
        '@nerv-iip/ui/file-preview': fileURLToPath(
          new URL('../../../../packages/ui/src/components/ui/file-preview/index.ts', import.meta.url),
        ),
        '@nerv-iip/ui': fileURLToPath(
          new URL('../../../../packages/ui/src/index.ts', import.meta.url),
        ),
        '@nerv-iip/ui-mobile': fileURLToPath(
          new URL('../../../../packages/ui-mobile/src/index.ts', import.meta.url),
        ),
      },
    },
  },
})
