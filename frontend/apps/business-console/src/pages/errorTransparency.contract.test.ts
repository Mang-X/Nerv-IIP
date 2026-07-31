import { readFileSync, readdirSync, statSync } from 'node:fs'
import { dirname, join, relative, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

/**
 * 「分层透传」的仓库级门禁（MAN-700 / #1289，规范见
 * `frontend/DESIGN/patterns/feedback-and-notifications.md` §1c）。
 *
 * 为什么要用扫描而不是逐页写 90 条同型断言：被吞掉的错误从来不是某一页的 bug，而是同一种
 * **写法**（只判 `error instanceof Error`、把 `error.message` 直接上屏）在 90 个页面里复制。
 * generated client 在 `throwOnError` 下抛的是**解析后的响应体对象**，这种写法会把所有 HTTP
 * 失败（含后端明确给了中文拒绝理由的 400）吞成猜测性兜底文案，或把英文 5xx 原文甩到界面上。
 * 这条门禁按写法拦，新页面照抄旧页面也拦得住。
 */
const SRC = resolve(dirname(fileURLToPath(import.meta.url)), '..')

/** 允许保留裸 `instanceof Error` 的地方，各有明确理由（不是「先放着」）。 */
const RAW_ERROR_ALLOWLIST = new Map<string, string>([
  ['utils/notify.ts', '分层透传的实现本身'],
  ['composables/lifecycleAction.ts', '只做类型判定，不取消息上屏'],
  ['composables/useFulfillmentTimeline.ts', '包装成领域错误类型 FulfillmentNodeError，不直接上屏'],
  ['composables/mes/useReceiptCreateForm.ts', '已先走 serverErrorMessage，仅作兜底取值'],
  [
    'pages/quality/inspection-tasks.vue',
    // ⚠️ 预存问题（待跟进）：这里靠 `errorValue instanceof Error` 判 403 走「无权限」空态，
    // 而 generated client 在 throwOnError 下抛的是响应体对象——这条判定对真实 403 其实**失效**，
    // 页面会退回普通失败态而不是无权限空态。不在本次范围内（本 PR 只做文案透传，不改空态语义）。
    '识别 403 走「无权限」空态，不把原文上屏；判定本身对响应体对象失效，预存问题待跟进',
  ],
])

/**
 * 允许直接调 `toast.*` 的文件：`notify.ts` 是实现本身；`pages/scheduling.vue` 的文案全部是
 * 写死的中文（成功提示与表单校验类拦截），不含任何服务端原文。
 */
const RAW_TOAST_ALLOWLIST = new Set(['utils/notify.ts', 'pages/scheduling.vue'])

function sourceFiles(dir: string): string[] {
  return readdirSync(dir).flatMap((entry) => {
    const full = join(dir, entry)
    if (statSync(full).isDirectory()) return sourceFiles(full)
    if (!/\.(vue|ts)$/.test(entry) || /\.(test|spec)\.ts$/.test(entry)) return []
    return [full]
  })
}

/** 注释里写「不要用 instanceof Error」不该被自己拦下来，扫描前先去掉注释行。 */
function stripComments(text: string) {
  return text
    .split('\n')
    .filter((line) => !/^\s*(\/\/|\/?\*)/.test(line))
    .join('\n')
}

const files = sourceFiles(SRC).map((full) => ({
  key: relative(SRC, full).replaceAll('\\', '/'),
  text: stripComments(readFileSync(full, 'utf8')),
}))

describe('业务前端错误透传契约', () => {
  it('扫描到了 business-console 的全部源文件（避免门禁自身空转）', () => {
    expect(files.length).toBeGreaterThan(200)
  })

  it('没有页面用 `instanceof Error` 判定错误形状——响应体对象会被整条吞掉', () => {
    const offenders = files
      .filter(({ key, text }) => !RAW_ERROR_ALLOWLIST.has(key) && text.includes('instanceof Error'))
      .map(({ key }) => key)

    expect(
      offenders,
      '请改用 notifyOperationFailure / notifyError / inlineErrorMessage（@/utils/notify）',
    ).toEqual([])
  })

  it('没有页面绕开 notify 直接调 toast——会跳过人话映射', () => {
    // 四个变体全拦：error/success 会跳过映射，warning/info 会绕过「业务页不直接调 toast」这条边界，
    // 同类提醒随后就会在各页各写一套（曾踩坑：ERP 采购申请转单的 toast.warning）。
    const offenders = files
      .filter(
        ({ key, text }) =>
          !RAW_TOAST_ALLOWLIST.has(key) && /\btoast\.(error|success|warning|info)\(/.test(text),
      )
      .map(({ key }) => key)

    expect(
      offenders,
      '请改用 notifySuccess / notifyWarning / notifyError / notifyOperationFailure（@/utils/notify）',
    ).toEqual([])
  })

  it('写操作的失败反馈带动作前缀：notifyOperationFailure 已铺到各业务域', () => {
    const domains = [
      'pages/erp/',
      'pages/wms/',
      'pages/mes/',
      'pages/master-data/',
      'pages/quality/',
      // 审批中心是跨域写面（裁决/委托/模板），此前不在名单里——#1298 的覆盖缺口就是这么漏过去的（#1311 / 台账 #29）。
      'pages/approval/',
    ]
    for (const domain of domains) {
      const covered = files.filter(
        ({ key, text }) => key.startsWith(domain) && text.includes('notifyOperationFailure('),
      )
      expect(covered.length, `${domain} 至少要有页面走 notifyOperationFailure`).toBeGreaterThan(0)
    }
  })
})
