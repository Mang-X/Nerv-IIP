import { describe, expect, it } from 'vitest'

const businessPages = import.meta.glob('./**/*.vue', {
  eager: true,
  import: 'default',
  query: '?raw',
}) as Record<string, string>
const screenPages = import.meta.glob('../../../screen/src/pages/**/*.vue', {
  eager: true,
  import: 'default',
  query: '?raw',
}) as Record<string, string>
const screenComponents = import.meta.glob('../../../screen/src/components/**/*.vue', {
  eager: true,
  import: 'default',
  query: '?raw',
}) as Record<string, string>
const screenLayouts = import.meta.glob('../../../screen/src/layouts/**/*.vue', {
  eager: true,
  import: 'default',
  query: '?raw',
}) as Record<string, string>

describe('2026-07-26 leadership walkthrough remediation', () => {
  it('does not leave a single KPI card isolated from a metric group', () => {
    for (const [path, source] of Object.entries(businessPages)) {
      const cardCount = source.match(/<NvMetricCard\b/g)?.length ?? 0
      const hasCompanionMetric = /<NvMetric(?:Strip|Ring)\b/.test(source)
      expect.soft(cardCount === 1 && !hasCompanionMetric, path).toBe(false)
    }
  })

  it('does not use gradient backgrounds on either walkthrough surface', () => {
    const sources = {
      ...businessPages,
      ...screenPages,
      ...screenComponents,
      ...screenLayouts,
    }
    for (const [path, source] of Object.entries(sources)) {
      expect
        .soft(source, `${path} contains a Tailwind gradient background`)
        .not.toMatch(/\bbg-gradient(?:-to-[a-z]+)?\b/)
      expect
        .soft(source, `${path} contains a CSS gradient background`)
        .not.toMatch(/background(?:-image)?\s*:\s*(?:linear|radial|repeating-linear)-gradient\(/s)
    }
  })

  it('keeps implementation-stage copy out of business pages', () => {
    for (const [path, source] of Object.entries(businessPages)) {
      expect.soft(source, path).not.toMatch(/过渡入口|正式页面|即将上线/)
    }
  })

  it('does not render an empty spare-parts table when the list request failed', () => {
    const source = businessPages['./maintenance/spare-parts.vue']
    expect(source).toContain('v-if="!listErrorMessage"')
    expect(source).toContain('<Empty v-if="listErrorMessage"')
    expect(source).toContain('数据来自维修工单的备件需求')
  })

  it('keeps implementation details out of screen-facing copy', () => {
    const sources = { ...screenPages, ...screenComponents, ...screenLayouts }
    for (const [path, source] of Object.entries(sources)) {
      const template = source.slice(source.indexOf('<template>'), source.lastIndexOf('</template>'))
      expect
        .soft(template, path)
        .not.toMatch(
          /待\s*#\d+|·\s*#\d+|historian|数据为\s*mock|后端接入|聚合端点|适配器聚合|接入中|读面|演示数据冒充|演示推算|作业域演示数据|演示模式/,
        )
    }
  })

  // MAN-698 走查非阻断池 · 批次 B（UX 与防呆）。每条对应一个台账号 / GH#1292 分项。
  describe('MAN-698 批次 B', () => {
    // 台账 #49：新建移动的类型下拉此前给了后端必拒的 receipt/issue，默认值还正是 receipt。
    it('库存新建移动不再硬编码移动类型，选项一律走受控值', () => {
      const source = businessPages['./inventory/movements.vue']
      expect(source).toContain('INVENTORY_MANUAL_MOVEMENT_TYPE_OPTIONS')
      expect(source).toContain('INVENTORY_MANUAL_MOVEMENT_DEFAULT_TYPE')
      // 幽灵值不许回来；transfer 不在此列——#1359 已在后端强制两腿配平，移库是合法选项。
      expect(source).not.toMatch(/<NvSelectItem value="(receipt|issue)"/)
      expect(source).not.toMatch(/['"]receipt['"]|['"]issue['"]/)
    })

    // #1359 的调拨两腿表单不许在后续改动里被顺手抹掉：选了移库就必须能填入库库位。
    it('选中移库时给出入库库位这一腿', () => {
      const source = businessPages['./inventory/movements.vue']
      expect(source).toContain('transferInLocationCode')
      expect(source).toContain('入库库位')
    })

    // GH#1292 第 7 项：发布弹框实测 970.75px > 950px 视口，没有内部滚动，主操作按钮够不着。
    it('工程「发布新版本」弹框都能内部滚动', () => {
      for (const page of [
        './engineering/routings.vue',
        './engineering/ebom.vue',
        './engineering/mbom.vue',
      ]) {
        expect(businessPages[page], page).toMatch(
          /<NvDialogContent class="max-h-\[85vh\] overflow-y-auto sm:max-w-3xl">/,
        )
      }
    })

    // GH#1292 第 2 项：分页口径的统计卡此前不带任何范围标注，会被读成全局 KPI。
    it('销售订单统计卡标注「本页」口径，全量口径只留页头总数', () => {
      const source = businessPages['./erp/sales/orders.vue']
      expect(source).toContain("label: '本页已释放订单'")
      expect(source).toContain("label: '本页订单金额'")
      expect(source).not.toContain("label: '已释放订单'")
    })

    // GH#1292 第 1 项：销售订单读面不带交期，交期取自本页已在取的紧急度读面，不新增契约。
    it('销售订单列表有交期列，且交期来自紧急度读面而非凭空构造', () => {
      const source = businessPages['./erp/sales/orders.vue']
      expect(source).toContain("header: '交期'")
      expect(source).toContain('timeCriticality?.dueUtc')
    })
  })

  it('keeps all three quality backlog groups inside the fixed-height screen panel', () => {
    const source = screenPages['../../../screen/src/pages/quality.vue']
    expect(source).toMatch(/\.qb-ib\s*\{[\s\S]*?gap:\s*4px/)
    expect(source).toMatch(/\.ib-meta\s*\{[\s\S]*?margin-top:\s*2px/)
    expect(source).toMatch(/\.ib-bar\s*\{[\s\S]*?margin-top:\s*4px/)
  })
})
