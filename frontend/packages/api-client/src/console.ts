export {
  getConsoleInstanceDetailQueryOptions,
  getConsoleOperationTaskQueryOptions,
  listConsoleInstancesQueryOptions,
  restartConsoleInstanceMutationOptions,
} from './generated/@pinia/colada.gen'

import type {
  NervIipContractsAppHubQueriesInstanceDetailResponse,
  NervIipContractsAppHubQueriesInstanceListItem,
  NervIipContractsAppHubQueriesInstanceListResponse,
  NervIipContractsOpsOperationTaskResponse,
  NervIipPlatformGatewayWebEndpointsOperationsRestartInstanceRequest,
} from './generated/types.gen'

export type InstanceDetailResponse = NervIipContractsAppHubQueriesInstanceDetailResponse
export type InstanceListItem = NervIipContractsAppHubQueriesInstanceListItem
export type InstanceListResponse = NervIipContractsAppHubQueriesInstanceListResponse
export type OperationTaskResponse = NervIipContractsOpsOperationTaskResponse
export type RestartInstanceRequest =
  NervIipPlatformGatewayWebEndpointsOperationsRestartInstanceRequest
