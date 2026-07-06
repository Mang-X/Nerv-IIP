<script setup lang="ts">
import { useRouter } from 'vue-router'
import { useAccessScope } from '@/access/useAccessScope'

// /workshop 入口：script-only 跳转到 persona 可见的第一个车间
// （单车间主任即其本车间；全厂管理默认首个车间，页内切换器可换）。
const scope = useAccessScope()
const router = useRouter()
const first = scope.visibleWorkshops[0]
void router.replace(first ? `/workshop/${first.id}` : '/')
</script>

<template>
  <div />
</template>
