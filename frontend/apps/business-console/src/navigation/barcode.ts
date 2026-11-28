import type { SideNav } from '@nerv-iip/app-shell'
import { FileTextIcon, HashIcon, ScanLineIcon, PrinterIcon } from '@lucide/vue'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'

export const barcodeSideNav: SideNav = [
  {
    items: [
      {
        title: '条码规则',
        icon: HashIcon,
        to: { path: '/barcode/rules' },
        requiredPermissions: [P.barcodeTemplatesManage],
      },
      {
        title: '标签模板',
        icon: FileTextIcon,
        to: { path: '/barcode/templates' },
        requiredPermissions: [P.barcodeTemplatesManage],
      },
      {
        title: '打印批次',
        icon: PrinterIcon,
        to: { path: '/barcode/print-batches' },
        requiredPermissions: [P.barcodePrint],
      },
      {
        title: '扫码记录',
        icon: ScanLineIcon,
        to: { path: '/barcode/scans' },
        requiredPermissions: [P.barcodeScansWrite],
      },
    ],
  },
]
