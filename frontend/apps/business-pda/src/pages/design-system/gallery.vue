<script setup lang="ts">
import {
  NvAppShellMobile,
  NvBottomSheet,
  NvListRow,
  NvMobileResult,
  NvScanBar,
} from '@nerv-iip/ui-mobile'
import { ref } from 'vue'

definePage({
  meta: { title: 'UI Mobile 组件库' },
})

const lastScan = ref('')
const sheetOpen = ref(false)
const listClicked = ref(false)
</script>

<template>
  <NvAppShellMobile>
    <template #header>
      <div class="px-4 py-3">
        <h1 class="text-lg font-semibold">UI Mobile 组件库</h1>
      </div>
    </template>

    <div class="space-y-6 p-4">
      <section data-testid="scan-section">
        <!-- 抽屉打开时停止抢焦（S3 契约）：否则 ScanBar 会把焦点从浮层抢回 -->
        <NvScanBar
          placeholder="扫描条码"
          :active="!sheetOpen"
          @scan="(v: string) => (lastScan = v)"
        />
        <p data-testid="scan-result" class="mt-1 text-sm text-muted-foreground">{{ lastScan }}</p>
      </section>

      <section data-testid="list-section">
        <NvListRow
          title="收货单 RO-2026-001"
          subtitle="待收货 · 3 行"
          @select="listClicked = true"
        />
        <NvListRow title="只读行" :interactive="false" />
        <p data-testid="list-clicked">{{ listClicked ? 'clicked' : 'idle' }}</p>
      </section>

      <section data-testid="sheet-section">
        <button
          data-testid="open-sheet"
          type="button"
          class="min-h-touch rounded-lg bg-primary px-4 text-primary-foreground"
          @click="sheetOpen = true"
        >
          打开抽屉
        </button>
        <NvBottomSheet
          :open="sheetOpen"
          title="选择库位"
          @update:open="(v: boolean) => (sheetOpen = v)"
        >
          <p>抽屉内容</p>
        </NvBottomSheet>
      </section>

      <section data-testid="result-success">
        <NvMobileResult status="success" title="过账成功" description="收货单已完成" />
      </section>
      <section data-testid="result-error">
        <NvMobileResult status="error" title="过账失败" />
      </section>
    </div>

    <template #footer>
      <div class="px-4 py-2 text-center text-sm text-muted-foreground">底部导航占位</div>
    </template>
  </NvAppShellMobile>
</template>
