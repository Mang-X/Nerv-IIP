import { defineConfig } from 'vitepress'

export default defineConfig({
  title: 'Nerv-IIP 产品文档',
  description: 'Nerv-IIP 业务平台上手指南、业务流程和当前能力边界',
  lang: 'zh-CN',
  cleanUrls: true,
  lastUpdated: true,

  themeConfig: {
    nav: [
      { text: '快速开始', link: '/getting-started/engineering-to-production', activeMatch: '/getting-started/' },
      { text: '流程图', link: '/processes/', activeMatch: '/processes/' },
      { text: 'GitHub', link: 'https://github.com/Mang-X/Nerv-IIP' },
    ],
    sidebar: [
      {
        text: '开始',
        items: [{ text: '产品主线', link: '/' }],
      },
      {
        text: '上手路径',
        items: [
          { text: '工程资料到生产版本', link: '/getting-started/engineering-to-production' },
          { text: '需求计划到完工入库', link: '/getting-started/planning-to-finished-goods' },
          { text: '仓储收发与库存闭环', link: '/getting-started/wms-inventory-cycle' },
        ],
      },
      {
        text: '业务流程',
        items: [{ text: '核心流程图', link: '/processes/' }],
      },
    ],
    search: { provider: 'local' },
    outline: { level: [2, 3], label: '本页目录' },
    docFooter: { prev: '上一页', next: '下一页' },
    returnToTopLabel: '返回顶部',
    sidebarMenuLabel: '菜单',
    lastUpdatedText: '最后更新',
  },
})
