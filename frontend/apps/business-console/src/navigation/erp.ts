import type { SideNav } from '@nerv-iip/app-shell'
import {
  ArrowRightLeftIcon,
  CoinsIcon,
  ClipboardListIcon,
  FileTextIcon,
  FileCheck2Icon,
  HandshakeIcon,
  LayersIcon,
  PackageCheckIcon,
  PackageIcon,
  ReceiptTextIcon,
  SearchIcon,
  WalletIcon,
} from '@lucide/vue'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'

export const erpSideNav: SideNav = [
  {
    label: '采购',
    items: [
      {
        title: '采购申请',
        icon: ClipboardListIcon,
        to: { path: '/erp' },
        requiredPermissions: [P.erpProcurementRead],
      },
      {
        title: '询价 RFQ',
        icon: SearchIcon,
        to: { path: '/erp/procurement/rfqs' },
        requiredPermissions: [P.erpProcurementRead],
      },
      {
        title: '供应商报价',
        icon: ReceiptTextIcon,
        to: { path: '/erp/procurement/supplier-quotations' },
        requiredPermissions: [P.erpProcurementRead],
      },
      {
        title: '采购订单',
        icon: PackageCheckIcon,
        to: { path: '/erp/procurement/purchase-orders' },
        requiredPermissions: [P.erpProcurementRead],
      },
      {
        title: '采购收货',
        icon: PackageIcon,
        to: { path: '/erp/procurement/receipts' },
        requiredPermissions: [P.erpProcurementRead],
      },
    ],
  },
  {
    label: '销售',
    items: [
      {
        title: '销售机会',
        icon: HandshakeIcon,
        to: { path: '/erp/sales' },
        requiredPermissions: [P.erpSalesRead],
      },
      {
        title: '销售报价',
        icon: FileTextIcon,
        to: { path: '/erp/sales/quotations' },
        requiredPermissions: [P.erpSalesRead],
      },
      {
        title: '销售订单',
        icon: ReceiptTextIcon,
        to: { path: '/erp/sales/orders' },
        requiredPermissions: [P.erpSalesRead],
      },
      {
        title: '销售发货',
        icon: PackageCheckIcon,
        to: { path: '/erp/sales/deliveries' },
        requiredPermissions: [P.erpSalesRead],
      },
    ],
  },
  {
    label: '财务',
    items: [
      {
        title: '财务摘要',
        icon: WalletIcon,
        to: { path: '/erp/finance' },
        requiredPermissions: [P.erpFinanceRead],
      },
      {
        title: 'AR/AP',
        icon: ArrowRightLeftIcon,
        to: { path: '/erp/finance/ar-ap' },
        requiredPermissions: [P.erpFinanceRead],
      },
      {
        title: '会计凭证',
        icon: FileCheck2Icon,
        to: { path: '/erp/finance/vouchers' },
        requiredPermissions: [P.erpFinanceRead],
      },
      {
        title: '成本候选',
        icon: LayersIcon,
        to: { path: '/erp/finance/cost-candidates' },
        requiredPermissions: [P.erpFinanceRead],
      },
      {
        title: '机器制造费用',
        icon: LayersIcon,
        to: { path: '/erp/finance/machine-overhead' },
        requiredPermissions: [P.erpFinanceRead],
      },
      {
        title: '机器制造费用率',
        icon: CoinsIcon,
        to: { path: '/erp/finance/machine-overhead-rates' },
        requiredPermissions: [P.erpFinanceRead],
      },
      {
        title: '工单成本差异',
        icon: LayersIcon,
        to: { path: '/erp/finance/cost-variance' },
        requiredPermissions: [P.erpFinanceRead],
      },
      {
        title: '工作中心费率',
        icon: CoinsIcon,
        to: { path: '/erp/finance/work-center-cost-rates' },
        requiredPermissions: [P.erpFinanceRead],
      },
    ],
  },
]
