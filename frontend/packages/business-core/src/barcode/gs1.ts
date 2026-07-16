/**
 * GS1-128 / GS1 DataMatrix 元素串（Application Identifier）解析——纯前端能力，
 * 用于 PDA 收货扫码自动带出批号/效期。仅覆盖收货常用 AI：
 *   (01) GTIN        14 位定长
 *   (11) 生产日期    YYMMDD 定长
 *   (17) 有效期      YYMMDD 定长
 *   (10) 批号/批次   变长（FNC1/GS 结束）
 *   (21) 序列号      变长（FNC1/GS 结束）
 *
 * 变长字段以 FNC1（ASCII 29，GS `\x1d`）分隔；扫码枪常配置为在变长 AI 后输出 GS。
 * 无 GS 时无法可靠切分相邻变长字段——此为已知限制，手输兜底覆盖。
 */

export interface Gs1Fields {
  gtin?: string
  /** 生产日期，规范化为 `YYYY-MM-DD`。 */
  productionDate?: string
  /** 有效期，规范化为 `YYYY-MM-DD`；DD=00 按 GS1 规则取当月最后一天。 */
  expiryDate?: string
  lotNo?: string
  serialNo?: string
  /** 原始去装饰后的元素串（便于诊断）。 */
  raw: string
}

const GS = String.fromCharCode(29)

/**
 * 已知定长 AI → 数据长度。只提取 01/11/17（见解析循环），其余为已知定长但不提取的
 * AI（SSCC/其它标识/其它日期/变体号）——列出长度以便「跳过」而非在遇到它们时停止，
 * 使单个不提取的定长 AI 不阻断后续 (10)/(17)/(21)。未列出的 AI 长度真未知，停止解析。
 */
const FIXED_LEN: Record<string, number> = {
  '00': 18, // SSCC
  '01': 14, // GTIN（提取）
  '02': 14, // 内含商品 GTIN
  '03': 14,
  '04': 16,
  '11': 6, // 生产日期（提取）
  '12': 6, // 到期付款日
  '13': 6, // 包装日期
  '15': 6, // 最佳食用日期
  '16': 6, // 销售截止日期
  '17': 6, // 有效期（提取）
  '20': 2, // 产品变体
}
/** 变长 AI（以 GS 或串尾结束）。 */
const VARIABLE_AIS = new Set(['10', '21'])

/** YYMMDD → `YYYY-MM-DD`；DD=00 取当月末；非真实日历日期返回 undefined。 */
function parseGs1Date(yymmdd: string): string | undefined {
  if (!/^\d{6}$/.test(yymmdd)) return undefined
  const yy = Number(yymmdd.slice(0, 2))
  const mm = Number(yymmdd.slice(2, 4))
  let dd = Number(yymmdd.slice(4, 6))
  if (mm < 1 || mm > 12) return undefined
  const year = 2000 + yy
  if (dd === 0) {
    // GS1：日为 00 表示当月最后一天。
    dd = new Date(Date.UTC(year, mm, 0)).getUTCDate()
  }
  // 按真实日历 round-trip 校验：Date.UTC 会把 2/31、4/31 静默进位到下个月，
  // 回读三项不一致即非法日期（含闰年 2/29 的正确判定），拒绝而非静默归一化。
  const probe = new Date(Date.UTC(year, mm - 1, dd))
  if (
    probe.getUTCFullYear() !== year ||
    probe.getUTCMonth() !== mm - 1 ||
    probe.getUTCDate() !== dd
  ) {
    return undefined
  }
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${year}-${pad(mm)}-${pad(dd)}`
}

/** 去除符号学标识符（如 `]C1`/`]d2`/`]Q3`）与前导 FNC1。 */
function stripPrefix(input: string): string {
  let s = input
  if (s.startsWith(']') && s.length >= 3) s = s.slice(3)
  while (s.startsWith(GS)) s = s.slice(1)
  return s
}

/**
 * 解析 GS1 元素串。识别到任一已知 AI 才返回对象，否则返回 null（非 GS1 码，
 * 调用方可把原值当作普通批号手输处理）。
 */
export function parseGs1(input: string | null | undefined): Gs1Fields | null {
  if (!input) return null
  const raw = stripPrefix(input.trim())
  const fields: Gs1Fields = { raw }
  let i = 0
  let matched = false

  while (i < raw.length) {
    if (raw[i] === GS) {
      i += 1
      continue
    }
    const ai = raw.slice(i, i + 2)
    if (!/^\d{2}$/.test(ai)) break // 不是可识别的 AI，停止解析
    i += 2

    if (VARIABLE_AIS.has(ai)) {
      let end = raw.indexOf(GS, i)
      if (end === -1) end = raw.length
      const value = raw.slice(i, end)
      i = end
      if (!value) break
      if (ai === '10') fields.lotNo = value
      else if (ai === '21') fields.serialNo = value
      matched = true
      continue
    }

    const len = FIXED_LEN[ai]
    if (len == null) break // 未支持的 AI，无法确定长度，停止
    const value = raw.slice(i, i + len)
    if (value.length < len) break
    i += len
    if (ai === '01') fields.gtin = value
    else if (ai === '11') {
      const d = parseGs1Date(value)
      if (d) fields.productionDate = d
    } else if (ai === '17') {
      const d = parseGs1Date(value)
      if (d) fields.expiryDate = d
    }
    matched = true
  }

  return matched ? fields : null
}
