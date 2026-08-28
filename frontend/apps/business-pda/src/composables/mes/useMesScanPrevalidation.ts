import {
  prevalidateBusinessConsoleMesContextScan,
  prevalidateBusinessConsoleMesMaterialScan,
  resolveBusinessConsoleBarcode,
  type BusinessConsoleBarcodeResolveCandidate,
  type BusinessConsoleBarcodeResolveEnvelope,
  type BusinessConsoleBarcodeResolveRequest,
  type BusinessConsoleMesContextScanPrevalidationRequest,
  type BusinessConsoleMesContextScanPrevalidationResponse,
  type BusinessConsoleMesMaterialScanPrevalidationRequest,
  type BusinessConsoleMesMaterialScanPrevalidationResponse,
} from '@nerv-iip/api-client'
import { computed, shallowRef, toValue, watch, type MaybeRefOrGetter } from 'vue'

import { describeRequestError } from '@/api/request-timeout'

export type MesScanPrevalidationStatus =
  | 'idle'
  | 'pending'
  | 'resolved'
  | 'ambiguous'
  | 'unknown'
  | 'unsupported'
  | 'forbidden'
  | 'rejected'
  | 'error'

export interface MesScanContext {
  workOrderId?: string
  operationTaskId?: string
}

export type MesScanAccepted =
  | {
      kind: 'work-order'
      candidate: BusinessConsoleBarcodeResolveCandidate
      workOrderId: string
    }
  | {
      kind: 'operation-task' | 'device' | 'personnel'
      candidate: BusinessConsoleBarcodeResolveCandidate
      workOrderId: string
      operationTaskId: string
      scannedObjectId: string
    }
  | {
      kind: 'material'
      candidate: BusinessConsoleBarcodeResolveCandidate
      workOrderId: string
      operationTaskId: string
      materialIssueRequestId: string
      materialId?: string | null
      materialLotId?: string | null
      materialQualification?: string | null
    }

interface MesScanPrevalidationOptions {
  organizationId: MaybeRefOrGetter<string>
  environmentId: MaybeRefOrGetter<string>
  context: MaybeRefOrGetter<MesScanContext>
  resolveBarcode?: (
    request: BusinessConsoleBarcodeResolveRequest,
  ) => Promise<BusinessConsoleBarcodeResolveEnvelope>
  prevalidateMaterial?: (
    request: BusinessConsoleMesMaterialScanPrevalidationRequest,
  ) => Promise<BusinessConsoleMesMaterialScanPrevalidationResponse>
  prevalidateContext?: (
    request: BusinessConsoleMesContextScanPrevalidationRequest,
  ) => Promise<BusinessConsoleMesContextScanPrevalidationResponse>
}

const REASON_MESSAGES: Record<string, string> = {
  'material-issue-request-not-found': '未找到该领料单，已阻止使用。',
  'work-order-mismatch': '该物料不属于当前工单，已阻止使用。',
  'mes-context-not-found': '当前工单工序已不存在或不在权限范围内。',
  'line-side-receipt-incomplete': '该物料批次尚未完成线边接收，不能用于当前工序。',
  'material-not-required': '该物料不是当前工序主料或已冻结替代料。',
  'material-lot-not-found': '当前线边未找到该物料批次。',
  'material-lot-expired': '该物料批次已过期，不能用于当前工序。',
  'material-lot-blocked': '该物料批次当前禁止移动或耗用。',
  'operation-task-mismatch': '扫码工序与当前工序不匹配。',
  'device-asset-mismatch': '设备与当前工序指派设备不匹配。',
  'personnel-mismatch': '工牌与当前工序指派人员不匹配。',
}

function normalized(value: string | undefined | null) {
  return value?.trim() ?? ''
}

function strongId(candidate: BusinessConsoleBarcodeResolveCandidate, name: string) {
  return normalized(candidate.strongIds?.[name])
}

function isForbidden(error: unknown) {
  if (!error || typeof error !== 'object') return false
  const value = error as { status?: number; response?: { status?: number } }
  return value.status === 403 || value.response?.status === 403
}

function errorText(error: unknown) {
  if (typeof error === 'string') return error
  if (
    error &&
    typeof error === 'object' &&
    typeof (error as { message?: unknown }).message === 'string'
  ) {
    return (error as { message: string }).message
  }
  return ''
}

function prevalidationFailureMessage(error: unknown) {
  const raw = errorText(error)
  if (/SOURCE_UNAVAILABLE/i.test(raw)) {
    return '扫码预校验来源暂不可用，已阻止当前操作，请稍后重试。'
  }
  return describeRequestError(error, '扫码预校验失败，已阻止当前操作，请稍后重试。').message
}

async function defaultResolveBarcode(request: BusinessConsoleBarcodeResolveRequest) {
  const response = await resolveBusinessConsoleBarcode({ body: request, throwOnError: true })
  return response.data
}

async function defaultPrevalidateMaterial(
  request: BusinessConsoleMesMaterialScanPrevalidationRequest,
) {
  const response = await prevalidateBusinessConsoleMesMaterialScan({
    body: request,
    throwOnError: true,
  })
  if (!response.data.success || !response.data.data) {
    throw new Error(response.data.message?.trim() || '物料扫码预校验未返回有效结果。')
  }
  return response.data.data
}

async function defaultPrevalidateContext(
  request: BusinessConsoleMesContextScanPrevalidationRequest,
) {
  const response = await prevalidateBusinessConsoleMesContextScan({
    body: request,
    throwOnError: true,
  })
  if (!response.data.success || !response.data.data) {
    throw new Error(response.data.message?.trim() || '工序上下文扫码预校验未返回有效结果。')
  }
  return response.data.data
}

export function useMesScanPrevalidation(options: MesScanPrevalidationOptions) {
  const status = shallowRef<MesScanPrevalidationStatus>('idle')
  const scannedValue = shallowRef('')
  const reasonCode = shallowRef<string | null>(null)
  const failureMessage = shallowRef('')
  const candidates = shallowRef<BusinessConsoleBarcodeResolveCandidate[]>([])
  const accepted = shallowRef<MesScanAccepted | null>(null)
  let generation = 0

  const resolveBarcode = options.resolveBarcode ?? defaultResolveBarcode
  const prevalidateMaterial = options.prevalidateMaterial ?? defaultPrevalidateMaterial
  const prevalidateContext = options.prevalidateContext ?? defaultPrevalidateContext

  function scope() {
    return {
      organizationId: normalized(toValue(options.organizationId)),
      environmentId: normalized(toValue(options.environmentId)),
    }
  }

  function context() {
    const value = toValue(options.context)
    return {
      workOrderId: normalized(value.workOrderId),
      operationTaskId: normalized(value.operationTaskId),
    }
  }

  function reset() {
    generation += 1
    status.value = 'idle'
    scannedValue.value = ''
    reasonCode.value = null
    failureMessage.value = ''
    candidates.value = []
    accepted.value = null
  }

  watch(
    [
      () => normalized(toValue(options.organizationId)),
      () => normalized(toValue(options.environmentId)),
      () => normalized(toValue(options.context).workOrderId),
      () => normalized(toValue(options.context).operationTaskId),
    ],
    reset,
    { flush: 'sync' },
  )

  function reject(code: string, fallback: string) {
    reasonCode.value = code
    failureMessage.value = REASON_MESSAGES[code] ?? fallback
    status.value = 'rejected'
    accepted.value = null
    return null
  }

  async function prevalidateCandidate(
    candidate: BusinessConsoleBarcodeResolveCandidate,
    currentGeneration: number,
  ): Promise<MesScanAccepted | null> {
    const currentScope = scope()
    const currentContext = context()

    if (candidate.objectType === 'mes-work-order') {
      const workOrderId = strongId(candidate, 'workOrderId')
      if (!workOrderId) {
        status.value = 'unsupported'
        return null
      }
      return { kind: 'work-order', candidate, workOrderId }
    }

    if (candidate.objectType === 'mes-operation') {
      const candidateWorkOrderId = strongId(candidate, 'workOrderId')
      const candidateOperationTaskId = strongId(candidate, 'operationTaskId')
      const workOrderId = currentContext.workOrderId || candidateWorkOrderId
      const operationTaskId = currentContext.operationTaskId || candidateOperationTaskId
      if (!candidateWorkOrderId || !candidateOperationTaskId || !workOrderId || !operationTaskId) {
        status.value = 'unsupported'
        return null
      }
      const response = await prevalidateContext({
        ...currentScope,
        workOrderId,
        operationTaskId,
        objectType: 'operationTask',
        scannedObjectId: candidateOperationTaskId,
      })
      if (currentGeneration !== generation) return null
      if (response.decision !== 'accepted') {
        return reject(response.reasonCode, '扫码工序与当前工单工序不匹配。')
      }
      return {
        kind: 'operation-task',
        candidate,
        workOrderId: response.workOrderId,
        operationTaskId: response.operationTaskId,
        scannedObjectId: response.scannedObjectId,
      }
    }

    if (candidate.objectType === 'equipment-device' || candidate.objectType === 'personnel') {
      if (!currentContext.workOrderId || !currentContext.operationTaskId) {
        return reject('mes-context-required', '请先选择当前工单和工序，再扫描设备或工牌。')
      }
      const isDevice = candidate.objectType === 'equipment-device'
      const scannedObjectId = strongId(candidate, isDevice ? 'deviceAssetId' : 'userId')
      if (!scannedObjectId) {
        status.value = 'unsupported'
        return null
      }
      const response = await prevalidateContext({
        ...currentScope,
        ...currentContext,
        objectType: isDevice ? 'deviceAsset' : 'personnel',
        scannedObjectId,
      })
      if (currentGeneration !== generation) return null
      if (response.decision !== 'accepted') {
        return reject(response.reasonCode, '扫码对象与当前工序上下文不匹配。')
      }
      return {
        kind: isDevice ? 'device' : 'personnel',
        candidate,
        workOrderId: response.workOrderId,
        operationTaskId: response.operationTaskId,
        scannedObjectId: response.scannedObjectId,
      }
    }

    if (candidate.objectType === 'mes-material-issue-request') {
      if (!currentContext.workOrderId || !currentContext.operationTaskId) {
        return reject('mes-context-required', '请先选择当前工单和工序，再扫描物料或批次。')
      }
      const materialIssueRequestId = strongId(candidate, 'materialIssueRequestId')
      if (!materialIssueRequestId) {
        status.value = 'unsupported'
        return null
      }
      const response = await prevalidateMaterial({
        ...currentScope,
        ...currentContext,
        materialIssueRequestId,
      })
      if (currentGeneration !== generation) return null
      if (response.decision !== 'accepted') {
        return reject(response.reasonCode, '该物料或批次未通过当前工序预校验。')
      }
      return {
        kind: 'material',
        candidate,
        workOrderId: response.workOrderId,
        operationTaskId: response.operationTaskId,
        materialIssueRequestId: response.materialIssueRequestId,
        materialId: response.materialId,
        materialLotId: response.materialLotId,
        materialQualification: response.materialQualification,
      }
    }

    status.value = 'unsupported'
    return null
  }

  async function selectCandidate(candidate: BusinessConsoleBarcodeResolveCandidate) {
    const currentGeneration = ++generation
    reasonCode.value = null
    failureMessage.value = ''
    accepted.value = null
    status.value = 'pending'
    try {
      const result = await prevalidateCandidate(candidate, currentGeneration)
      if (currentGeneration !== generation) return null
      if (result) {
        accepted.value = result
        status.value = 'resolved'
      }
      return result
    } catch (error) {
      if (currentGeneration !== generation) return null
      failureMessage.value = prevalidationFailureMessage(error)
      status.value = isForbidden(error) ? 'forbidden' : 'error'
      return null
    }
  }

  async function scan(value: string) {
    const currentGeneration = ++generation
    const currentScope = scope()
    const scanned = value.trim()
    scannedValue.value = scanned
    reasonCode.value = null
    failureMessage.value = ''
    candidates.value = []
    accepted.value = null

    if (!scanned || !currentScope.organizationId || !currentScope.environmentId) {
      status.value = 'error'
      failureMessage.value = '缺少扫码内容、组织或环境，已阻止当前操作。'
      return null
    }

    status.value = 'pending'
    try {
      const envelope = await resolveBarcode({
        ...currentScope,
        scannedValue: scanned,
        pageIndex: 1,
        pageSize: 20,
      })
      if (currentGeneration !== generation) return null
      if (!envelope.success || !envelope.data) {
        status.value = 'error'
        failureMessage.value = envelope.message?.trim() || '扫码解析未返回有效结果。'
        return null
      }

      reasonCode.value = envelope.data.reasonCode ?? null
      const resolvedCandidates = envelope.data.candidates ?? []
      if (envelope.data.status === 'ambiguous') {
        candidates.value = resolvedCandidates
        status.value = 'ambiguous'
        return null
      }
      if (envelope.data.status === 'unknown') {
        status.value = 'unknown'
        return null
      }
      if (envelope.data.status === 'unsupported') {
        status.value = 'unsupported'
        return null
      }
      if (envelope.data.status === 'forbidden') {
        status.value = 'forbidden'
        return null
      }
      if (envelope.data.status !== 'resolved' || resolvedCandidates.length !== 1) {
        status.value = 'unsupported'
        return null
      }

      const result = await prevalidateCandidate(resolvedCandidates[0]!, currentGeneration)
      if (currentGeneration !== generation) return null
      if (result) {
        accepted.value = result
        status.value = 'resolved'
      }
      return result
    } catch (error) {
      if (currentGeneration !== generation) return null
      failureMessage.value = prevalidationFailureMessage(error)
      status.value = isForbidden(error) ? 'forbidden' : 'error'
      return null
    }
  }

  const pending = computed(() => status.value === 'pending')
  const message = computed(() => {
    if (failureMessage.value) return failureMessage.value
    switch (status.value) {
      case 'pending':
        return '正在解析并预校验扫码内容…'
      case 'resolved':
        return accepted.value?.kind === 'material'
          ? '物料与批次已通过当前工单工序预校验。'
          : '扫码对象已通过当前工单工序预校验。'
      case 'ambiguous':
        return '找到多个候选，请手动选择；系统不会猜测。'
      case 'unknown':
        return '无法确认该扫码内容，已阻止当前操作。'
      case 'unsupported':
        return '已识别，但当前页面不支持使用该对象。'
      case 'forbidden':
        return '当前账号无权解析或预校验该扫码内容。'
      case 'rejected':
        return '该扫码对象未通过当前上下文预校验。'
      case 'error':
        return '扫码解析或预校验服务暂不可用，已阻止当前操作。'
      default:
        return ''
    }
  })

  return {
    status,
    pending,
    scannedValue,
    reasonCode,
    message,
    candidates,
    accepted,
    scan,
    selectCandidate,
    reset,
  }
}
