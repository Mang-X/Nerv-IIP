<script setup lang="ts">
import type { Component } from 'vue'
import { ChevronRightIcon } from '@lucide/vue'
import { NvCard } from '@nerv-iip/ui'
import { RouterLink } from 'vue-router'

/**
 * 工作台「业务域磁贴」——取代此前把每个域的每个页面平铺成一片文字链接的做法。
 *
 * 收纳单位是**业务域**而不是页面：域内页面在进入该域后由左侧导航承担，工作台首屏
 * 只需要给出"去哪个域"这一层决策，因此磁贴数量恒定（与顶部 T 型导航同源），高度
 * 一致，不会随权限扩张把首屏撑成链接海。每块磁贴带域图标（导航必带图标），落点是
 * 当前角色在该域**第一个有权限**的页面，避免落到无权访问的域首页。
 */
export interface WorkbenchDomainTile {
  id: string
  title: string
  icon?: Component
  to: string
  moduleCount: number
}

defineProps<{
  tiles: WorkbenchDomainTile[]
}>()
</script>

<template>
  <!--
    磁贴区与行动区共同吸收首屏剩余高度（各自 flex-1），而不是把空白全压给其中一段：
    全给磁贴会让每格变成一个图标的空盒子，全给行动区则会在没有出口的卡片下方留一大片
    白。两段一起长，磁贴仍保持一眼扫完的紧凑比例。
  -->
  <NvCard class="flex flex-1 flex-col overflow-hidden bg-gradient-to-t from-primary/5 to-card p-0">
    <div class="flex items-center justify-between gap-3 border-b px-5 py-3">
      <h2 class="text-sm font-semibold text-foreground">业务域入口</h2>
      <span class="text-xs tabular-nums text-muted-foreground">{{ tiles.length }} 个域</span>
    </div>

    <div
      v-if="tiles.length > 0"
      class="grid flex-1 auto-rows-fr grid-cols-2 gap-3 p-4 sm:grid-cols-3 lg:grid-cols-4 2xl:grid-cols-6"
    >
      <RouterLink
        v-for="tile in tiles"
        :key="tile.id"
        class="group flex min-h-16 items-center gap-3 rounded-lg border bg-card px-3 transition-colors hover:border-primary/40 hover:bg-accent"
        :to="tile.to"
      >
        <span
          class="grid size-9 flex-none place-items-center rounded-[10px] bg-muted text-muted-foreground transition-colors group-hover:bg-brand/10 group-hover:text-brand-strong"
        >
          <component :is="tile.icon" v-if="tile.icon" class="size-[18px]" aria-hidden="true" />
        </span>
        <span class="min-w-0 flex-1">
          <span class="block truncate text-sm font-medium text-foreground">{{ tile.title }}</span>
          <span class="block text-xs tabular-nums text-muted-foreground"
            >{{ tile.moduleCount }} 个模块</span
          >
        </span>
        <ChevronRightIcon
          class="size-4 shrink-0 text-muted-foreground transition-transform group-hover:translate-x-0.5"
          aria-hidden="true"
        />
      </RouterLink>
    </div>

    <div v-else class="px-5 py-6 text-center">
      <p class="text-sm font-medium text-foreground">当前角色没有可进入的业务域</p>
      <p class="mt-1 text-sm text-muted-foreground">
        请联系管理员为该账号分配业务权限后再回到工作台。
      </p>
    </div>
  </NvCard>
</template>
