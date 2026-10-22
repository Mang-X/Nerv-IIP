import { defineAsyncComponent } from 'vue'
import { BUSINESS_PERMISSION_CODES } from '@/permissions'
import type { DirectoryCreator } from '../directoryCreators'

export default {
  permission: BUSINESS_PERMISSION_CODES.inventoryLocationsManage,
  dialog: defineAsyncComponent(() => import('@/components/inventory/LocationFormDialog.vue')),
} satisfies DirectoryCreator
