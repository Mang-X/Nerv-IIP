<script setup lang="ts">
import { onMounted } from 'vue'
import { useRouter } from 'vue-router'

// 旧「工程版本」过渡页已下线：MBOM / 工艺路线 / EBOM 均已拆为独立页
// （/engineering/mbom、/engineering/routings、/engineering/ebom），生产版本为示范页。
// /engineering 旧入口重定向到生产版本，避免旧书签 / 历史链接落到 404。
definePage({ redirect: '/engineering/production-versions' })

// 兜底：若运行时未走路由级 redirect，挂载后立即替换到生产版本。
const router = useRouter()
onMounted(() => {
  void router.replace('/engineering/production-versions')
})
</script>

<template>
  <div />
</template>
