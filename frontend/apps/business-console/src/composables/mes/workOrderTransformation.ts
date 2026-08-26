import { errorStatusCode, serverErrorMessage } from '@/utils/notify'

export interface WorkOrderTransformationSource {
  workOrderId: string
  skuId?: string | null
  productionVersionId?: string | null
  uomCode?: string | null
  quantity?: number | null
  status?: string | null
}

export interface SplitTargetDraft {
  workOrderId: string
  quantity: string
}

export interface SplitValidationInput {
  sourceWorkOrderId: string
  sourceQuantity?: number | null
  targets: SplitTargetDraft[]
  reason: string
}

export interface MergeValidationInput {
  sources: WorkOrderTransformationSource[]
  targetWorkOrderId: string
  reason: string
}

function decimalParts(value: string | number) {
  const raw = String(value).trim()
  const match = raw.match(/^([+-]?)(?:(\d+)(?:\.(\d*))?|\.(\d+))(?:e([+-]?\d+))?$/i)
  if (!match) return undefined

  const integerPart = match[2] ?? ''
  const fractionPart = match[3] ?? match[4] ?? ''
  const exponent = Number(match[5] ?? 0)
  let scale = fractionPart.length - exponent
  const digits = `${integerPart}${fractionPart}` || '0'
  let unscaled = BigInt(`${match[1] === '-' ? '-' : ''}${digits}`)
  if (scale < 0) {
    unscaled *= 10n ** BigInt(-scale)
    scale = 0
  }
  return { scale, unscaled }
}

function scaleTo(value: { scale: number; unscaled: bigint }, scale: number) {
  return value.unscaled * 10n ** BigInt(scale - value.scale)
}

function decimalEqual(left: string | number, right: string | number) {
  const leftParts = decimalParts(left)
  const rightParts = decimalParts(right)
  if (!leftParts || !rightParts) return false
  const scale = Math.max(leftParts.scale, rightParts.scale)
  return scaleTo(leftParts, scale) === scaleTo(rightParts, scale)
}

function decimalToNumber(unscaled: bigint, scale: number) {
  const negative = unscaled < 0n
  const digits = (negative ? -unscaled : unscaled).toString()
  if (scale === 0) return Number(`${negative ? '-' : ''}${digits}`)
  const padded = digits.padStart(scale + 1, '0')
  const splitAt = padded.length - scale
  return Number(`${negative ? '-' : ''}${padded.slice(0, splitAt)}.${padded.slice(splitAt)}`)
}

export function parsePositiveQuantity(input: string | number | null | undefined) {
  if (typeof input === 'string' && input.trim() === '') return undefined
  const value = typeof input === 'number' ? input : Number(input)
  if (!Number.isFinite(value) || value <= 0 || !decimalParts(input as string | number)) {
    return undefined
  }
  return value
}

export function sumQuantities(values: Array<string | number>) {
  const parts = values.map(decimalParts)
  if (parts.some((part) => !part)) return Number.NaN
  const validParts = parts as Array<{ scale: number; unscaled: bigint }>
  const scale = Math.max(0, ...validParts.map((part) => part.scale))
  const total = validParts.reduce((sum, part) => sum + scaleTo(part, scale), 0n)
  return decimalToNumber(total, scale)
}

export function validateSplitInput(input: SplitValidationInput) {
  const errors: string[] = []
  if (input.targets.length < 2) errors.push('至少填写两个子工单。')

  const sourceId = input.sourceWorkOrderId.trim()
  const seenIds = new Set<string>()
  input.targets.forEach((target, index) => {
    const targetId = target.workOrderId.trim()
    if (!targetId) errors.push(`请填写第 ${index + 1} 个子工单标识。`)
    else if (targetId === sourceId) errors.push('子工单标识不能与源工单相同。')
    else if (seenIds.has(targetId)) errors.push('子工单标识不能重复。')
    seenIds.add(targetId)
  })

  const quantities = input.targets.map((target) => parsePositiveQuantity(target.quantity))
  quantities.forEach((quantity, index) => {
    if (quantity === undefined) errors.push(`第 ${index + 1} 个子工单数量必须大于 0。`)
  })
  if (
    input.targets.length >= 2 &&
    quantities.every((quantity): quantity is number => quantity !== undefined)
  ) {
    if (input.sourceQuantity === undefined || input.sourceQuantity === null) {
      errors.push('尚未取得源工单数量，不能提交。')
    } else if (!decimalEqual(sumQuantities(quantities), input.sourceQuantity)) {
      errors.push(`拆分后数量必须等于源工单数量 ${input.sourceQuantity}。`)
    }
  }
  if (!input.reason.trim()) errors.push('请填写拆分原因。')
  else if (input.reason.trim().length > 500) errors.push('拆分原因不能超过 500 个字符。')
  return [...new Set(errors)]
}

export function validateMergeInput(input: MergeValidationInput) {
  const errors: string[] = []
  if (input.sources.length < 2) errors.push('至少选择两个源工单。')

  const sourceIds = input.sources.map((source) => source.workOrderId.trim())
  if (sourceIds.some((id) => !id)) errors.push('合并源工单缺少工单标识。')
  if (new Set(sourceIds).size !== sourceIds.length) errors.push('合并源工单不能重复。')

  const targetId = input.targetWorkOrderId.trim()
  if (!targetId) errors.push('请填写新的合并目标工单标识。')
  else if (sourceIds.includes(targetId)) errors.push('合并目标必须是新的工单标识。')

  const first = input.sources[0]
  if (first) {
    const sameContext = input.sources.every(
      (source) =>
        source.skuId === first.skuId &&
        source.productionVersionId === first.productionVersionId &&
        source.uomCode === first.uomCode,
    )
    if (!sameContext) errors.push('只能合并 SKU、生产版本和单位都相同的工单。')

    const transformable = input.sources.every((source) =>
      ['created', 'released'].includes((source.status ?? '').toLowerCase()),
    )
    if (!transformable) errors.push('只有已创建或已下达状态的工单可以合并。')
  }

  if (!input.reason.trim()) errors.push('请填写合并原因。')
  else if (input.reason.trim().length > 500) errors.push('合并原因不能超过 500 个字符。')
  return [...new Set(errors)]
}

export function isTransformationConflict(error: unknown) {
  if (errorStatusCode(error) === 409) return true
  const text = serverErrorMessage(error)
  return /\b409\b|conflict|idempotency|work-order-transformation/i.test(text)
}
