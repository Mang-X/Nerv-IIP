import {
  createBusinessConsoleMaintenanceSparePartMutationOptions,
  listBusinessConsoleMaintenanceInspectionsQueryOptions,
  listBusinessConsoleMaintenanceSparePartsQueryOptions,
  recordBusinessConsoleMaintenanceInspectionMutationOptions,
  type BusinessConsoleCreateMaintenanceSparePartRequest,
  type BusinessConsoleMaintenanceInspectionItem,
  type BusinessConsoleMaintenanceInspectionListEnvelope,
  type BusinessConsoleMaintenanceSparePartItem,
  type BusinessConsoleMaintenanceSparePartListEnvelope,
  type BusinessConsoleRecordMaintenanceInspectionRequest,
} from '@nerv-iip/api-client'
import { useMutation, useQuery } from '@pinia/colada'
import { computed } from 'vue'
import {
  refetchWithBusinessContext,
  withBusinessContextEnabled,
} from '../shared/businessContextBinding'
import {
  defaultFilters,
  listItems,
  listTotal,
  type MaintenanceListFilters,
} from './maintenanceShared'

export function useMaintenanceInspections(initialFilters: Partial<MaintenanceListFilters> = {}) {
  const filters = defaultFilters(initialFilters)
  const inspectionsQuery = useQuery(() =>
    withBusinessContextEnabled(
      listBusinessConsoleMaintenanceInspectionsQueryOptions({
        query: {
          organizationId: filters.organizationId,
          environmentId: filters.environmentId,
          skip: filters.skip,
          take: filters.take,
        },
      }),
      filters,
    ),
  )

  const recordMutation = useMutation({
    ...recordBusinessConsoleMaintenanceInspectionMutationOptions(),
    onSuccess() {
      void refetchWithBusinessContext(filters, inspectionsQuery)
    },
  })

  return {
    filters,
    inspections: computed<BusinessConsoleMaintenanceInspectionItem[]>(() =>
      listItems<BusinessConsoleMaintenanceInspectionItem>(
        inspectionsQuery.data.value as BusinessConsoleMaintenanceInspectionListEnvelope | undefined,
      ),
    ),
    inspectionsError: inspectionsQuery.error,
    inspectionsPending: inspectionsQuery.isLoading,
    inspectionsTotal: computed(() =>
      listTotal(
        inspectionsQuery.data.value as BusinessConsoleMaintenanceInspectionListEnvelope | undefined,
      ),
    ),
    refreshInspections: () => refetchWithBusinessContext(filters, inspectionsQuery),
    recordInspection: (body: BusinessConsoleRecordMaintenanceInspectionRequest) =>
      recordMutation.mutateAsync({ body }),
    recordInspectionPending: recordMutation.isLoading,
    recordInspectionError: recordMutation.error,
  }
}

export function useMaintenanceSpareParts(initialFilters: Partial<MaintenanceListFilters> = {}) {
  const filters = defaultFilters(initialFilters)
  const sparePartsQuery = useQuery(() =>
    withBusinessContextEnabled(
      listBusinessConsoleMaintenanceSparePartsQueryOptions({
        query: {
          organizationId: filters.organizationId,
          environmentId: filters.environmentId,
          skip: filters.skip,
          take: filters.take,
        },
      }),
      filters,
    ),
  )

  const createMutation = useMutation({
    ...createBusinessConsoleMaintenanceSparePartMutationOptions(),
    onSuccess() {
      void refetchWithBusinessContext(filters, sparePartsQuery)
    },
  })

  return {
    filters,
    spareParts: computed<BusinessConsoleMaintenanceSparePartItem[]>(() =>
      listItems<BusinessConsoleMaintenanceSparePartItem>(
        sparePartsQuery.data.value as BusinessConsoleMaintenanceSparePartListEnvelope | undefined,
      ),
    ),
    sparePartsError: sparePartsQuery.error,
    sparePartsPending: sparePartsQuery.isLoading,
    sparePartsTotal: computed(() =>
      listTotal(
        sparePartsQuery.data.value as BusinessConsoleMaintenanceSparePartListEnvelope | undefined,
      ),
    ),
    refreshSpareParts: () => refetchWithBusinessContext(filters, sparePartsQuery),
    createSparePart: (body: BusinessConsoleCreateMaintenanceSparePartRequest) =>
      createMutation.mutateAsync({ body }),
    createSparePartPending: createMutation.isLoading,
    createSparePartError: createMutation.error,
  }
}
