import {
  deleteBusinessConsoleTelemetryConnectorConfigMutationOptions,
  listBusinessConsoleTelemetryConnectorConfigsQueryOptions,
  saveBusinessConsoleTelemetryConnectorConfigMutationOptions,
  testBusinessConsoleTelemetryConnectorConnectionMutationOptions,
  type TelemetryConnectorConfigInput,
  type TelemetryConnectionTestInput,
} from '@nerv-iip/api-client'
import { useMutation, useQuery } from '@pinia/colada'
import { computed } from 'vue'
import { useBusinessContextStore } from '@/stores/businessContext'
import { hasBusinessContext } from '../shared/businessContextBinding'
import { toContextQuery, unwrapData } from './telemetryShared'

export function useConnectorConfigs() {
  const context = useBusinessContextStore()
  const query = useQuery(() => ({
    ...listBusinessConsoleTelemetryConnectorConfigsQueryOptions({ query: toContextQuery(context) }),
    enabled: hasBusinessContext(context),
    autoRefetch: () => 5_000,
  }))
  const save = useMutation(saveBusinessConsoleTelemetryConnectorConfigMutationOptions())
  const remove = useMutation(deleteBusinessConsoleTelemetryConnectorConfigMutationOptions())
  const test = useMutation(testBusinessConsoleTelemetryConnectorConnectionMutationOptions())

  return {
    configs: computed(() => unwrapData(query.data.value) ?? []),
    configsError: query.error,
    configsPending: query.isLoading,
    refreshConfigs: () => query.refetch(),
    async saveConfig(config: TelemetryConnectorConfigInput) {
      const response = await save.mutateAsync({ body: { ...toContextQuery(context), config } })
      if (!response.success) throw response
      await query.refetch()
    },
    async deleteConfig(connectorId: string) {
      const response = await remove.mutateAsync({
        path: { connectorId },
        body: toContextQuery(context),
      })
      if (!response.success) throw response
      await query.refetch()
    },
    async testConnection(connection: TelemetryConnectionTestInput) {
      const response = await test.mutateAsync({ body: { ...toContextQuery(context), connection } })
      if (!response.success) throw response
      return response.data
    },
  }
}
