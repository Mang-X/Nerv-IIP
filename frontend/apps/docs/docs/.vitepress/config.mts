import { defineConfig } from 'vitepress'

// 导航信息架构口径见 docs/adr/0020-product-docs-information-architecture.md：
// 四象限（教程/操作指南/概念解释/参考）+ 角色入口；processes/ 归概念解释象限且 URL 不迁移。
export default defineConfig({
  title: 'Nerv-IIP 产品文档',
  description: 'Nerv-IIP 业务平台上手指南、业务流程和当前能力边界',
  lang: 'zh-CN',
  cleanUrls: true,
  lastUpdated: true,

  themeConfig: {
    nav: [
      { text: '按角色入门', link: '/roles/', activeMatch: '/roles/' },
      {
        text: '教程',
        link: '/getting-started/engineering-to-production',
        activeMatch: '/getting-started/',
      },
      { text: '操作指南', link: '/how-to/', activeMatch: '/how-to/' },
      { text: '概念解释', link: '/explanation/', activeMatch: '/(explanation|processes)/' },
      { text: '参考', link: '/reference/', activeMatch: '/reference/' },
      { text: 'GitHub', link: 'https://github.com/Mang-X/Nerv-IIP' },
    ],
    sidebar: [
      {
        text: '开始',
        items: [{ text: '产品主线', link: '/' }],
      },
      {
        text: '按角色入门',
        items: [
          { text: '角色总览', link: '/roles/' },
          { text: '计划员', link: '/roles/planner' },
          { text: '班组长', link: '/roles/team-leader' },
          { text: '仓管员', link: '/roles/warehouse' },
          { text: '质检员', link: '/roles/quality-inspector' },
          { text: '设备工程师', link: '/roles/equipment-engineer' },
          { text: '采购与财务', link: '/roles/procurement-finance' },
        ],
      },
      {
        text: '教程',
        items: [
          { text: '工程资料到生产版本', link: '/getting-started/engineering-to-production' },
          { text: '需求计划到完工入库', link: '/getting-started/planning-to-finished-goods' },
          { text: '仓储收发与库存闭环', link: '/getting-started/wms-inventory-cycle' },
        ],
      },
      {
        text: '操作指南',
        items: [{ text: '指南总览', link: '/how-to/' }],
      },
      {
        text: '概念解释',
        items: [
          { text: '解释总览', link: '/explanation/' },
          { text: '核心流程图', link: '/processes/' },
        ],
      },
      {
        text: '参考',
        items: [{ text: '参考总览', link: '/reference/' }],
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
