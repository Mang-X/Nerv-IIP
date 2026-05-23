<script setup lang="ts">
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import {
  Badge,
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@nerv-iip/ui'

definePage({
  meta: {
    requiresAuth: true,
    title: 'routes.business',
  },
})

const businessServices = [
  { name: 'BusinessMasterData', scope: 'Layer 0 master data', issue: '#72/#127' },
  { name: 'BusinessProductEngineering', scope: 'Engineering documents, items, BOM, routing and changes', issue: '#127' },
  { name: 'BusinessInventory', scope: 'Locations, ledgers, movements and stock count', issue: '#131' },
  { name: 'BusinessQuality', scope: 'Inspection plans, records and nonconformance facts', issue: '#132' },
  { name: 'BusinessMES', scope: 'Work orders, operation tasks and execution reporting', issue: '#135' },
  { name: 'BusinessDemandPlanning', scope: 'Demand sources, MPS, MRP and pegging', issue: '#128' },
  { name: 'BarcodeLabel', scope: 'Barcode rules, label templates, print batches and scans', issue: '#133' },
  { name: 'BusinessApproval', scope: 'Business approval templates, chains, steps and decisions', issue: '#134' },
  { name: 'WMS', scope: 'Inbound, outbound, warehouse tasks and WCS boundary', issue: '#136' },
  { name: 'BusinessIndustrialTelemetry', scope: 'Telemetry tags, device snapshots, alarms and summaries', issue: '#129' },
  { name: 'BusinessMaintenance', scope: 'Work orders, PM plans, inspections, downtime and spare parts', issue: '#130' },
  { name: 'BusinessERP', scope: 'Procurement, sales and finance MVP facts', issue: '#137/#138/#139' },
] as const
</script>

<template>
  <DefaultLayout>
    <div class="grid gap-4">
      <div class="flex flex-wrap items-end justify-between gap-3">
        <div>
          <p class="text-xs font-bold uppercase text-primary">Business platform</p>
          <h1 class="text-xl font-semibold text-foreground">Business MVP status</h1>
          <p class="mt-1 max-w-3xl text-sm text-muted-foreground">
            Delivered backend capabilities, acceptance readiness and current scope boundaries for
            the business platform MVP.
          </p>
        </div>
        <Badge variant="secondary">12 services delivered</Badge>
      </div>

      <div class="grid gap-4 xl:grid-cols-[minmax(0,1fr)_22rem]">
        <Card class="min-w-0">
          <CardHeader>
            <CardTitle>Delivered backend services</CardTitle>
            <CardDescription>
              Current readiness snapshot for the business service layer.
            </CardDescription>
          </CardHeader>
          <CardContent>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Service</TableHead>
                  <TableHead>Capability</TableHead>
                  <TableHead class="w-28">Issue</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                <TableRow
                  v-for="service in businessServices"
                  :key="service.name"
                  data-test="business-service"
                >
                  <TableCell class="font-medium">{{ service.name }}</TableCell>
                  <TableCell class="text-muted-foreground">{{ service.scope }}</TableCell>
                  <TableCell>
                    <Badge variant="outline">{{ service.issue }}</Badge>
                  </TableCell>
                </TableRow>
              </TableBody>
            </Table>
          </CardContent>
        </Card>

        <div class="grid content-start gap-4">
          <Card>
            <CardHeader>
              <CardTitle>#77 Full-chain acceptance</CardTitle>
              <CardDescription>Next mainline for cross-service business demo readiness.</CardDescription>
            </CardHeader>
            <CardContent class="grid gap-2 text-sm text-muted-foreground">
              <p>Connect the delivered MVP facts into a verifiable cross-service path.</p>
              <p>Use the acceptance harness baseline as the expansion point for the seven chains.</p>
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle>#78 Gantt/RFC reference is excluded</CardTitle>
              <CardDescription>Reference material only, not part of this backend/domain route.</CardDescription>
            </CardHeader>
            <CardContent class="text-sm text-muted-foreground">
              #78 remains outside this Console MVP entry; no timeline editor, Gantt view or RFC
              workflow is introduced here.
            </CardContent>
          </Card>
        </div>
      </div>
    </div>
  </DefaultLayout>
</template>
