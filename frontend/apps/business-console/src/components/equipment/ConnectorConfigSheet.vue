<script setup lang="ts">
import type {
  TelemetryConnectionTestResult,
  TelemetryConnectorConfigItem,
  TelemetryConnectorMapping,
} from '@nerv-iip/api-client'
import {
  NvButton,
  NvInput,
  NvSheet,
  NvSheetContent,
  NvSheetDescription,
  NvSheetHeader,
  NvSheetTitle,
} from '@nerv-iip/ui'
import { reactive, ref, watch } from 'vue'
import { useEquipmentDeviceCatalog } from '@/composables/equipment/useEquipmentPickerCatalog'
import { useConnectorConfigs } from '@/composables/equipment/useConnectorConfigs'
import { notifyOperationFailure, notifySuccess } from '@/utils/notify'

const props = defineProps<{ config: TelemetryConnectorConfigItem | null }>()
const open = defineModel<boolean>('open', { required: true })
const emit = defineEmits<{ saved: [] }>()
const { saveConfig, testConnection, configs } = useConnectorConfigs()
const { devicePublicIdByCode } = useEquipmentDeviceCatalog()
const busy = ref(false)
const result = ref<TelemetryConnectionTestResult | null>()
const error = ref('')
const form = reactive({
  connectorId: '',
  displayName: '',
  protocol: 'modbus',
  endpoint: '',
  pollIntervalSeconds: 2,
  isEnabled: true,
  mappings: [] as TelemetryConnectorMapping[],
})
const protocols = [
  { value: 'modbus', label: 'Modbus TCP' },
  { value: 'mqtt', label: 'MQTT' },
  { value: 'opcua', label: 'OPC UA' },
]

watch(open, (value) => {
  if (!value) return
  Object.assign(form, {
    connectorId: '',
    displayName: '',
    protocol: 'modbus',
    endpoint: '',
    pollIntervalSeconds: 2,
    isEnabled: true,
    ...props.config,
    mappings: props.config?.mappings?.map((mapping) => ({ ...mapping })) ?? [],
  })
  error.value = ''
})
watch(form, () => {
  result.value = null
})

function addMapping() {
  form.mappings.push({
    deviceAssetId: '',
    tagKey: '',
    unitCode: '',
    bucketSeconds: 30,
    unitId: 1,
    address: 40001,
    dataType: 'Float32',
  })
}

function applyTemplate(protocol: 'modbus' | 'mqtt') {
  const modbus = protocol === 'modbus'
  const deviceAssetId = modbus ? 'DEV-CTG-01' : 'DEV-TST-01'
  const mappings: TelemetryConnectorMapping[] = modbus
    ? [
        {
          deviceAssetId,
          tagKey: 'bath-temperature',
          unitCode: '°C',
          unitId: 3,
          address: 40001,
          dataType: 'Float32',
        },
        {
          deviceAssetId,
          tagKey: 'bath-ph',
          unitCode: 'pH',
          unitId: 3,
          address: 40003,
          dataType: 'Float32',
        },
      ]
    : [
        {
          deviceAssetId,
          tagKey: 'damping-force',
          unitCode: 'N',
          topicFilter: 'factory/DEV-TST-01/telemetry',
          valueJsonPath: '$.dampingForce',
        },
        {
          deviceAssetId,
          tagKey: 'oil-temperature',
          unitCode: '°C',
          topicFilter: 'factory/DEV-TST-01/telemetry',
          valueJsonPath: '$.oilTemperature',
        },
      ]
  Object.assign(form, {
    connectorId: `dev-${protocol}-${modbus ? 'coating' : 'test'}`,
    displayName: modbus ? '电泳涂装槽' : '减振器阻尼试验台',
    protocol,
    endpoint: `tcp://127.0.0.1:${modbus ? 15020 : 11883}`,
    mappings: mappings.map((mapping) => ({ ...mapping, bucketSeconds: 30 })),
  })
}

async function probe() {
  if (!form.endpoint.trim()) {
    error.value = '请填写连接地址'
    return
  }
  error.value = ''
  busy.value = true
  try {
    result.value = await testConnection({
      protocol: form.protocol,
      endpoint: form.endpoint,
      mapping: form.mappings[0],
    })
  } catch (cause) {
    notifyOperationFailure('测试连接失败', cause, '测试连接失败，请稍后重试。')
  } finally {
    busy.value = false
  }
}

async function save() {
  if (!form.mappings.length) {
    error.value = '请至少添加一个测点映射'
    return
  }
  if (
    !props.config &&
    configs.value.some((config) => config.connectorId === form.connectorId.trim())
  ) {
    error.value = '连接器编号已存在'
    return
  }
  busy.value = true
  try {
    const mappings = form.mappings.map((mapping) => {
      const device = mapping.deviceAssetId?.trim() ?? ''
      return { ...mapping, deviceAssetId: devicePublicIdByCode.value.get(device) ?? device }
    })
    await saveConfig({
      connectorId: form.connectorId.trim(),
      displayName: form.displayName.trim(),
      protocol: form.protocol,
      endpoint: form.endpoint.trim(),
      pollIntervalSeconds: form.pollIntervalSeconds,
      isEnabled: form.isEnabled,
      mappings,
    })
    notifySuccess('连接配置已保存')
    emit('saved')
    open.value = false
  } catch (cause) {
    notifyOperationFailure('保存连接配置失败', cause, '保存连接配置失败，请稍后重试。')
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <NvSheet v-model:open="open">
    <NvSheetContent class="overflow-y-auto sm:max-w-2xl">
      <NvSheetHeader>
        <NvSheetTitle>{{ config ? '编辑连接器' : '新建连接器' }}</NvSheetTitle>
        <NvSheetDescription>配置现场连接与采集点位映射。</NvSheetDescription>
      </NvSheetHeader>
      <form class="grid gap-4 p-4" @submit.prevent="save">
        <fieldset :disabled="busy" class="grid gap-4">
          <div v-if="!config" class="flex flex-wrap gap-2">
            <NvButton type="button" size="sm" variant="outline" @click="applyTemplate('modbus')"
              >开发模拟模板 · DEV-CTG-01</NvButton
            >
            <NvButton type="button" size="sm" variant="outline" @click="applyTemplate('mqtt')"
              >开发模拟模板 · DEV-TST-01</NvButton
            >
          </div>
          <div class="grid grid-cols-2 gap-3 text-sm">
            <label
              >连接器编号<NvInput v-model="form.connectorId" :readonly="!!config" required
            /></label>
            <label>名称<NvInput v-model="form.displayName" required /></label>
            <label
              >协议
              <select
                v-model="form.protocol"
                class="block h-9 w-full rounded-md border bg-background px-2"
              >
                <option v-for="protocol in protocols" :key="protocol.value" :value="protocol.value">
                  {{ protocol.label }}
                </option>
              </select>
            </label>
            <label
              >轮询周期（秒）<NvInput
                v-model.number="form.pollIntervalSeconds"
                type="number"
                min="1"
                required
            /></label>
            <label class="col-span-2">连接地址<NvInput v-model="form.endpoint" required /></label>
            <label class="flex items-center gap-2"
              ><input v-model="form.isEnabled" type="checkbox" /> 启用采集</label
            >
          </div>
          <div class="flex items-center gap-3">
            <NvButton type="button" size="sm" variant="outline" @click="probe">测试连接</NvButton>
            <p
              v-if="result"
              role="status"
              class="text-sm"
              :class="{ 'text-destructive': !result.reachable }"
            >
              {{ result.reachable ? '可达' : '不可达' }} · {{ result.latencyMs }} ms
              <span v-if="result.samplePreview != null">
                · 实测 {{ result.samplePreview }} {{ form.mappings[0]?.unitCode }}</span
              >
              <span v-if="!result.reachable"> · {{ result.message }}</span>
            </p>
          </div>
          <div class="flex items-center justify-between">
            <h3 class="font-semibold">测点映射</h3>
            <NvButton type="button" variant="outline" size="sm" @click="addMapping"
              >添加测点</NvButton
            >
          </div>
          <div
            v-for="(mapping, index) in form.mappings"
            :key="index"
            class="grid grid-cols-2 gap-3 rounded-md border p-3 text-sm"
          >
            <label>设备编号<NvInput v-model="mapping.deviceAssetId" required /></label>
            <label>点位编码<NvInput v-model="mapping.tagKey" required /></label>
            <label>单位<NvInput v-model="mapping.unitCode" required /></label>
            <label
              >聚合周期（秒）<NvInput
                v-model.number="mapping.bucketSeconds"
                type="number"
                min="1"
                required
            /></label>
            <template v-if="form.protocol === 'modbus'">
              <label
                >从站号<NvInput
                  v-model.number="mapping.unitId"
                  type="number"
                  min="1"
                  max="247"
                  required
              /></label>
              <label
                >寄存器地址<NvInput
                  v-model.number="mapping.address"
                  type="number"
                  min="40001"
                  max="49999"
                  required
              /></label>
              <label
                >寄存器类型<select
                  v-model="mapping.dataType"
                  class="block h-9 w-full rounded-md border bg-background px-2"
                >
                  <option>Float32</option>
                  <option>UInt16</option>
                </select></label
              >
            </template>
            <template v-else-if="form.protocol === 'mqtt'">
              <label
                >订阅主题<NvInput
                  :model-value="mapping.topicFilter ?? undefined"
                  @update:model-value="mapping.topicFilter = String($event)"
                  required
              /></label>
              <label
                >取值路径<NvInput
                  :model-value="mapping.valueJsonPath ?? undefined"
                  @update:model-value="mapping.valueJsonPath = String($event)"
                  required
              /></label>
            </template>
            <label v-else
              >节点标识<NvInput
                :model-value="mapping.nodeId ?? undefined"
                @update:model-value="mapping.nodeId = String($event)"
                required
            /></label>
            <NvButton
              type="button"
              size="sm"
              variant="ghost"
              class="col-start-2 justify-self-end"
              @click="form.mappings.splice(index, 1)"
              >移除测点</NvButton
            >
          </div>
        </fieldset>
        <p v-if="error" role="alert" class="text-sm text-destructive">{{ error }}</p>
        <NvButton type="submit" :disabled="busy" class="justify-self-end">保存配置</NvButton>
      </form>
    </NvSheetContent>
  </NvSheet>
</template>
