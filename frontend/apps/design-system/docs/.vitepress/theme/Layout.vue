<script setup lang="ts">
import { ThemePicker } from '@nerv-iip/ui'
import DefaultTheme from 'vitepress/theme'

// Wrap the default layout and drop our runtime accent picker into the nav.
// VitePress already owns the light/dark toggle (it flips `html.dark`, which our
// tokens follow); ThemePicker only writes `--brand`, so the two don't collide.
const { Layout } = DefaultTheme
</script>

<template>
  <Layout>
    <template #nav-bar-content-after>
      <ClientOnly>
        <ThemePicker class="ds-doc-accent ms-1" />
      </ClientOnly>
    </template>
  </Layout>
</template>

<style>
/* keep the injected accent control vertically centered in the nav */
.ds-doc-accent {
  display: inline-flex;
  align-items: center;
}
</style>
