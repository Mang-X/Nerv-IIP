import {
  createConsoleIamRoleMutationOptions,
  createConsoleIamUserMutationOptions,
  disableConsoleIamUserMutationOptions,
  listConsoleIamPermissionsQueryOptions,
  listConsoleIamRolesQueryOptions,
  listConsoleIamSessionsQueryOptions,
  listConsoleIamUsersQueryOptions,
  resetConsoleIamUserPasswordMutationOptions,
  revokeConsoleIamSessionMutationOptions,
  updateConsoleIamRolePermissionsMutationOptions,
  updateConsoleIamUserMutationOptions,
  type ConsoleIamPermissionResponse,
  type ConsoleIamRoleResponse,
  type ConsoleIamSessionResponse,
  type ConsoleIamUserResponse,
} from '@nerv-iip/api-client'
import { useMutation, useQuery, useQueryCache, type UseQueryEntry } from '@pinia/colada'
import { computed, reactive } from 'vue'
import { toConsoleIamError } from '@/api/iam'

const DEFAULT_PAGE_INDEX = 1
const DEFAULT_PAGE_SIZE = 20
const ignoreBackgroundError = (_error: unknown) => {}

export interface IamListFilters {
  filterEnabled?: boolean
  filterRevoked?: boolean
  filterSearch?: string
  pageIndex: number
  pageSize: number
  sortBy?: string
  sortOrder?: string
}

type IamListQuery = {
  filterEnabled?: boolean | null
  filterRevoked?: boolean | null
  filterSearch?: string | null
  pageIndex?: number | null
  pageSize?: number | null
  sortBy?: string | null
  sortOrder?: string | null
}

type ListEnvelope<T> = { data?: { items?: T[]; totalCount?: number } | null; success?: boolean }
type Envelope<T> = { data?: T | null; success?: boolean }

function createDefaultFilters(): IamListFilters {
  return reactive({
    pageIndex: DEFAULT_PAGE_INDEX,
    pageSize: DEFAULT_PAGE_SIZE,
  })
}

function toListQuery(filters: IamListFilters): IamListQuery {
  return {
    ...optionalQuery('filterEnabled', filters.filterEnabled),
    ...optionalQuery('filterRevoked', filters.filterRevoked),
    ...optionalQuery('filterSearch', filters.filterSearch),
    pageIndex: filters.pageIndex,
    pageSize: filters.pageSize,
    ...optionalQuery('sortBy', filters.sortBy),
    ...optionalQuery('sortOrder', filters.sortOrder),
  }
}

function optionalQuery<TKey extends keyof IamListQuery>(
  key: TKey,
  value: IamListQuery[TKey],
) {
  return value === undefined || value === '' ? {} : { [key]: value }
}

function unwrapEnvelope<T>(envelope: Envelope<T> | undefined): T | undefined {
  if (!envelope?.success) {
    return undefined
  }

  return envelope.data ?? undefined
}

function listItems<T>(envelope: ListEnvelope<T> | undefined): T[] {
  return unwrapEnvelope(envelope)?.items ?? []
}

function listTotalCount<T>(envelope: ListEnvelope<T> | undefined): number {
  return unwrapEnvelope(envelope)?.totalCount ?? 0
}

function isIamListEntry(id: string) {
  return (entry: UseQueryEntry) => {
    const keyParts = Array.isArray(entry.key) ? entry.key : [entry.key]

    return keyParts.some((part) => {
      return typeof part === 'object' && part !== null && '_id' in part && part._id === id
    })
  }
}

function invalidateIamList(queryCache: ReturnType<typeof useQueryCache>, id: string) {
  return queryCache.invalidateQueries({ predicate: isIamListEntry(id) })
}

export function useIamUsers() {
  const filters = createDefaultFilters()
  const queryCache = useQueryCache()

  const listQuery = useQuery(() =>
    listConsoleIamUsersQueryOptions({
      query: toListQuery(filters),
    }),
  )

  const createUserMutation = useMutation({
    ...createConsoleIamUserMutationOptions(),
    onSuccess() {
      void invalidateIamList(queryCache, 'listConsoleIamUsers').catch(ignoreBackgroundError)
    },
  })
  const updateUserMutation = useMutation({
    ...updateConsoleIamUserMutationOptions(),
    onSuccess() {
      void invalidateIamList(queryCache, 'listConsoleIamUsers').catch(ignoreBackgroundError)
    },
  })
  const disableUserMutation = useMutation({
    ...disableConsoleIamUserMutationOptions(),
    onSuccess() {
      void invalidateIamList(queryCache, 'listConsoleIamUsers').catch(ignoreBackgroundError)
    },
  })
  const resetUserPasswordMutation = useMutation({
    ...resetConsoleIamUserPasswordMutationOptions(),
    onSuccess() {
      void invalidateIamList(queryCache, 'listConsoleIamUsers').catch(ignoreBackgroundError)
    },
  })

  return {
    createUser: createUserMutation.mutateAsync,
    createUserError: computed(() => toOptionalConsoleIamError(createUserMutation.error.value)),
    createUserPending: createUserMutation.isLoading,
    disableUser: disableUserMutation.mutateAsync,
    disableUserError: computed(() => toOptionalConsoleIamError(disableUserMutation.error.value)),
    disableUserPending: disableUserMutation.isLoading,
    filters,
    refreshUsers: listQuery.refetch,
    resetUserPassword: resetUserPasswordMutation.mutateAsync,
    resetUserPasswordError: computed(() =>
      toOptionalConsoleIamError(resetUserPasswordMutation.error.value),
    ),
    resetUserPasswordPending: resetUserPasswordMutation.isLoading,
    totalCount: computed(() => listTotalCount<ConsoleIamUserResponse>(listQuery.data.value)),
    updateUser: updateUserMutation.mutateAsync,
    updateUserError: computed(() => toOptionalConsoleIamError(updateUserMutation.error.value)),
    updateUserPending: updateUserMutation.isLoading,
    users: computed(() => listItems<ConsoleIamUserResponse>(listQuery.data.value)),
    usersError: computed(() => toOptionalConsoleIamError(listQuery.error.value)),
    usersPending: listQuery.isLoading,
  }
}

export function useIamRoles() {
  const filters = createDefaultFilters()
  const queryCache = useQueryCache()

  const listQuery = useQuery(() =>
    listConsoleIamRolesQueryOptions({
      query: toListQuery(filters),
    }),
  )
  const permissionsQuery = useQuery(() => listConsoleIamPermissionsQueryOptions())
  const createRoleMutation = useMutation({
    ...createConsoleIamRoleMutationOptions(),
    onSuccess() {
      void invalidateIamList(queryCache, 'listConsoleIamRoles').catch(ignoreBackgroundError)
    },
  })
  const updateRolePermissionsMutation = useMutation({
    ...updateConsoleIamRolePermissionsMutationOptions(),
    onSuccess() {
      void invalidateIamList(queryCache, 'listConsoleIamRoles').catch(ignoreBackgroundError)
    },
  })

  return {
    createRole: createRoleMutation.mutateAsync,
    createRoleError: computed(() => toOptionalConsoleIamError(createRoleMutation.error.value)),
    createRolePending: createRoleMutation.isLoading,
    filters,
    permissions: computed<ConsoleIamPermissionResponse[]>(
      () => unwrapEnvelope(permissionsQuery.data.value)?.items ?? [],
    ),
    permissionsError: computed(() => toOptionalConsoleIamError(permissionsQuery.error.value)),
    permissionsPending: permissionsQuery.isLoading,
    refreshPermissions: permissionsQuery.refetch,
    refreshRoles: listQuery.refetch,
    roles: computed(() => listItems<ConsoleIamRoleResponse>(listQuery.data.value)),
    rolesError: computed(() => toOptionalConsoleIamError(listQuery.error.value)),
    rolesPending: listQuery.isLoading,
    totalCount: computed(() => listTotalCount<ConsoleIamRoleResponse>(listQuery.data.value)),
    updateRolePermissions: updateRolePermissionsMutation.mutateAsync,
    updateRolePermissionsError: computed(() =>
      toOptionalConsoleIamError(updateRolePermissionsMutation.error.value),
    ),
    updateRolePermissionsPending: updateRolePermissionsMutation.isLoading,
  }
}

export function useIamSessions() {
  const filters = createDefaultFilters()
  const queryCache = useQueryCache()

  const listQuery = useQuery(() =>
    listConsoleIamSessionsQueryOptions({
      query: toListQuery(filters),
    }),
  )
  const revokeSessionMutation = useMutation({
    ...revokeConsoleIamSessionMutationOptions(),
    onSuccess() {
      void invalidateIamList(queryCache, 'listConsoleIamSessions').catch(ignoreBackgroundError)
    },
  })

  return {
    filters,
    refreshSessions: listQuery.refetch,
    revokeSession: revokeSessionMutation.mutateAsync,
    revokeSessionError: computed(() => toOptionalConsoleIamError(revokeSessionMutation.error.value)),
    revokeSessionPending: revokeSessionMutation.isLoading,
    sessions: computed(() => listItems<ConsoleIamSessionResponse>(listQuery.data.value)),
    sessionsError: computed(() => toOptionalConsoleIamError(listQuery.error.value)),
    sessionsPending: listQuery.isLoading,
    totalCount: computed(() => listTotalCount<ConsoleIamSessionResponse>(listQuery.data.value)),
  }
}

function toOptionalConsoleIamError(error: unknown) {
  return error ? toConsoleIamError(error) : undefined
}
