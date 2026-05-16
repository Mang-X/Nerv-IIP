<script setup lang="ts">
import { AppShell } from '@nerv-iip/app-shell'

const navItems = [{ label: '实例', href: '/' }]
</script>

<template>
  <AppShell title="Nerv-IIP" :nav-items="navItems">
    <slot />
  </AppShell>
</template>
