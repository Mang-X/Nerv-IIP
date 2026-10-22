<script setup lang="ts">
/**
 * 交班附件（车间拍照）。
 *
 * 一张照片要走完整条 tus 通路才算「加上了」——建会话 → HEAD → PATCH → 复查 offset →
 * complete，成功后拿到的 `fileId/fileName/contentType/sizeBytes` 才是能随交班单提交的附件行。
 * 所以这里在 complete 回执到手之前**不往列表里加行**：先加行再上传会让操作工以为照片已经
 * 在单子上了，而实际上建单时那个 fileId 根本不存在。
 *
 * `capture="environment"` 让 Android WebView 直接开后置相机；桌面浏览器会退化成文件选择，
 * 走查时用选文件同样能验通这条字节通路。
 */
import { NvMobileButton, NvMobileTag } from '@nerv-iip/ui-mobile'
import { Camera } from '@lucide/vue'
import { ref } from 'vue'
import { describeRequestError } from '@/api/request-timeout'
import {
  formatAttachmentSize,
  type ShiftHandoverAttachment,
} from '@/composables/useBusinessShiftHandover'

const props = defineProps<{
  upload: (file: File) => Promise<ShiftHandoverAttachment>
  disabled?: boolean
  disabledReason?: string
}>()

const attachments = defineModel<ShiftHandoverAttachment[]>('attachments', { required: true })

const fileInput = ref<HTMLInputElement>()
const uploading = ref(false)
const uploadError = ref('')

function pickPhoto() {
  if (props.disabled || uploading.value) return
  uploadError.value = ''
  fileInput.value?.click()
}

async function onFileSelected(event: Event) {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  // 先清空 value：同一张照片连拍两次时 change 不会重复触发，清掉才能再选同一个文件。
  input.value = ''
  if (!file) return

  uploading.value = true
  uploadError.value = ''
  try {
    const attachment = await props.upload(file)
    attachments.value = [...attachments.value, attachment]
  } catch (error) {
    uploadError.value = describeRequestError(error, '照片上传失败，请重试。').message
  } finally {
    uploading.value = false
  }
}

function removeAttachment(index: number) {
  attachments.value = attachments.value.filter((_, i) => i !== index)
}
</script>

<template>
  <section class="space-y-2 rounded-xl border border-border bg-card p-3" data-testid="attachments">
    <div class="flex items-center gap-2">
      <h2 class="text-[15px] font-medium text-foreground">现场照片</h2>
      <NvMobileTag size="sm" variant="brand">{{ attachments.length }}</NvMobileTag>
    </div>

    <ul v-if="attachments.length" class="space-y-2" data-testid="attachment-rows">
      <li
        v-for="(attachment, index) in attachments"
        :key="attachment.fileId ?? `attachment-${index}`"
        class="rounded-lg border border-border bg-background px-3 py-2"
      >
        <p class="truncate text-sm font-medium text-foreground">{{ attachment.fileName }}</p>
        <p class="text-xs text-muted-foreground">
          {{ attachment.contentType }} · {{ formatAttachmentSize(attachment.sizeBytes) }}
        </p>
        <NvMobileButton
          variant="text"
          size="sm"
          class="mt-1 px-0"
          :data-testid="`remove-attachment-${index}`"
          @click="removeAttachment(index)"
          >移除</NvMobileButton
        >
      </li>
    </ul>

    <input
      ref="fileInput"
      type="file"
      accept="image/jpeg,image/png"
      capture="environment"
      class="hidden"
      data-testid="photo-input"
      @change="onFileSelected"
    />
    <NvMobileButton
      variant="outline"
      block
      data-testid="capture-photo"
      :disabled="disabled || uploading"
      @click="pickPhoto"
    >
      <Camera class="size-4" aria-hidden="true" />
      {{ uploading ? '照片上传中…' : '拍照并上传' }}
    </NvMobileButton>
    <p
      v-if="uploadError"
      role="alert"
      data-testid="attachment-error"
      class="text-sm text-destructive"
    >
      {{ uploadError }}
    </p>
    <p v-else-if="disabled && disabledReason" class="text-xs text-muted-foreground">
      {{ disabledReason }}
    </p>
    <p v-else class="text-xs text-muted-foreground">
      只支持 JPG / PNG，单张不超过 20 MB；照片上传成功后才会出现在上面的列表里。
    </p>
  </section>
</template>
