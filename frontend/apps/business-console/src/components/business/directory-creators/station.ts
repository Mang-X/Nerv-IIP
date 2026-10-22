import { defineAsyncComponent } from 'vue'
import { BUSINESS_PERMISSION_CODES } from '@/permissions'
import type { DirectoryCreator } from '../directoryCreators'

export default {
  permission: BUSINESS_PERMISSION_CODES.masterDataResourcesManage,
  dialog: defineAsyncComponent(() => import('@/components/masterData/StationCreateDialog.vue')),
} satisfies DirectoryCreator
