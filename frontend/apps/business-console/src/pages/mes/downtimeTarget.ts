import type { BusinessConsoleMesOperationTaskRow } from '@nerv-iip/api-client'

export type DowntimeTarget = {
  key: string
  workOrderId: string
  operationTaskId: string
  workCenterId: string
  deviceAssetId: string
  operationTask: BusinessConsoleMesOperationTaskRow
  label: string
}
