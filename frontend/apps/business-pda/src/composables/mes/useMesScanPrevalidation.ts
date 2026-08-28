import {
  prevalidateBusinessConsoleMesContextScan,
  prevalidateBusinessConsoleMesMaterialScan,
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
import { isForbiddenRequestError, usePdaBarcodeResolver } from '@/composables/usePdaBarcodeResolver'

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

export type MesScanAcceptedKind = MesScanAccepted['kind']

interface MesScanPrevalidationOptions {
  organizationId: MaybeRefOrGetter<string>
  environmentId: MaybeRefOrGetter<string>
  context: MaybeRefOrGetter<MesScanContext>
  acceptedKinds?: MaybeRefOrGetter<readonly MesScanAcceptedKind[]>
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
  const prevalidationStatus = shallowRef<MesScanPrevalidationStatus>('idle')
  const prevalidationReasonCode = shallowRef<string | null>(null)
  const failureMessage = shallowRef('')
  const accepted = shallowRef<MesScanAccepted | null>(null)
  let generation = 0

  const resolver = usePdaBarcodeResolver({
    organizationId: options.organizationId,
    environmentId: options.environmentId,
    resolveBarcode: options.resolveBarcode,
  })
  const prevalidateMaterial = options.prevalidateMaterial ?? defaultPrevalidateMaterial
  const prevalidateContext = options.prevalidateContext ?? defaultPrevalidateContext
  const acceptedKinds = () =>
    toValue(
      options.acceptedKinds ?? ['work-order', 'operation-task', 'device', 'personnel', 'material'],
    )

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

  function resetPrevalidation() {
    generation += 1
    prevalidationStatus.value = 'idle'
    prevalidationReasonCode.value = null
    failureMessage.value = ''
    accepted.value = null
  }

  function reset() {
    resetPrevalidation()
    resolver.cancel()
  }

  watch(
    [
      () => normalized(toValue(options.context).workOrderId),
      () => normalized(toValue(options.context).operationTaskId),
    ],
    reset,
    { flush: 'sync' },
  )
  watch(
    resolver.status,
    (value) => {
      if (value === 'idle' && prevalidationStatus.value !== 'idle') resetPrevalidation()
    },
    { flush: 'sync' },
  )

  const status = computed<MesScanPrevalidationStatus>(() =>
    prevalidationStatus.value === 'idle'
      ? (resolver.status.value as MesScanPrevalidationStatus)
      : prevalidationStatus.value,
  )
  const reasonCode = computed(() => prevalidationReasonCode.value ?? resolver.reasonCode.value)

  function reject(code: string, fallback: string) {
    prevalidationReasonCode.value = code
    failureMessage.value = REASON_MESSAGES[code] ?? fallback
    prevalidationStatus.value = 'rejected'
    accepted.value = null
    return null
  }

  function candidateKind(
    candidate: BusinessConsoleBarcodeResolveCandidate,
  ): MesScanAcceptedKind | null {
    if (candidate.objectType === 'mes-work-order') return 'work-order'
    if (candidate.objectType === 'mes-operation') return 'operation-task'
    if (candidate.objectType === 'equipment-device') return 'device'
    if (candidate.objectType === 'personnel') return 'personnel'
    if (candidate.objectType === 'mes-material-issue-request') return 'material'
    return null
  }

  async function prevalidateCandidate(
    candidate: BusinessConsoleBarcodeResolveCandidate,
    currentGeneration: number,
  ): Promise<MesScanAccepted | null> {
    const kind = candidateKind(candidate)
    if (!kind || !acceptedKinds().includes(kind)) {
      prevalidationStatus.value = 'unsupported'
      return null
    }
    const currentScope = scope()
    const currentContext = context()

    if (candidate.objectType === 'mes-work-order') {
      const workOrderId = strongId(candidate, 'workOrderId')
      if (!workOrderId) {
        prevalidationStatus.value = 'unsupported'
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
        prevalidationStatus.value = 'unsupported'
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
        prevalidationStatus.value = 'unsupported'
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
        prevalidationStatus.value = 'unsupported'
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

    prevalidationStatus.value = 'unsupported'
    return null
  }

  async function validateCandidate(
    candidate: BusinessConsoleBarcodeResolveCandidate,
    currentGeneration = ++generation,
  ) {
    prevalidationReasonCode.value = null
    failureMessage.value = ''
    accepted.value = null
    prevalidationStatus.value = 'pending'
    try {
      const result = await prevalidateCandidate(candidate, currentGeneration)
      if (currentGeneration !== generation) return null
      if (result) {
        accepted.value = result
        prevalidationStatus.value = 'resolved'
      }
      return result
    } catch (error) {
      if (currentGeneration !== generation) return null
      failureMessage.value = prevalidationFailureMessage(error)
      prevalidationStatus.value = isForbiddenRequestError(error) ? 'forbidden' : 'error'
      return null
    }
  }

  async function selectCandidate(candidate: BusinessConsoleBarcodeResolveCandidate) {
    return validateCandidate(resolver.chooseCandidate(candidate))
  }

  async function scan(value: string) {
    resetPrevalidation()
    const currentGeneration = generation
    prevalidationStatus.value = 'pending'
    const candidate = await resolver.resolveCandidate(value)
    if (currentGeneration !== generation) return null
    if (!candidate) {
      prevalidationStatus.value = 'idle'
      return null
    }
    return validateCandidate(candidate, currentGeneration)
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
    scannedValue: resolver.scannedValue,
    reasonCode,
    message,
    candidates: resolver.candidates,
    accepted,
    scan,
    selectCandidate,
    reset,
  }
}
