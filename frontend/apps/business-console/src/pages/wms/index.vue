<script setup lang="ts">
import { onMounted } from 'vue'
import { useRouter } from 'vue-router'

// /wms 旧入口重定向到收货入库，避免旧书签 / 历史链接 / 外部跳转落到 404。
definePage({ redirect: '/wms/inbound' })

// 兜底：若运行时未走路由级 redirect，挂载后立即替换到收货入库。
const router = useRouter()
onMounted(() => {
  void router.replace('/wms/inbound')
})
</script>

<template>
  <div />
</template>
