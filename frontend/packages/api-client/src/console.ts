export {
  getConsoleInstanceDetailQueryOptions,
  getConsoleOperationTaskQueryOptions,
  listConsoleInstancesQueryOptions,
  restartConsoleInstanceMutationOptions,
} from './generated/@pinia/colada.gen'

import type {
  GetConsoleInstanceDetailResponse,
  ListConsoleInstancesResponse,
  NervIipContractsOpsOperationTaskResponse,
  NervIipPlatformGatewayWebEndpointsOperationsRestartInstanceRequest,
} from './generated/types.gen'

export type InstanceDetailResponse = GetConsoleInstanceDetailResponse
export type InstanceListItem = never
export type InstanceListResponse = ListConsoleInstancesResponse
export type OperationTaskResponse = NervIipContractsOpsOperationTaskResponse
export type RestartInstanceRequest = NervIipPlatformGatewayWebEndpointsOperationsRestartInstanceRequest
