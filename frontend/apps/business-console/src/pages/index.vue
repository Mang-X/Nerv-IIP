<script setup lang="ts">
const domainSlices = [
  { id: 'master-data', label: 'MasterData', status: 'Facade ready' },
  { id: 'inventory', label: 'Inventory', status: 'Facade ready' },
  { id: 'quality', label: 'Quality', status: 'Facade ready' },
  { id: 'mes', label: 'MES', status: 'Facade ready' },
]
</script>

<template>
  <main class="business-console-shell">
    <section class="business-console-shell__header" aria-labelledby="business-console-title">
      <p class="business-console-shell__eyebrow">Nerv-IIP</p>
      <h1 id="business-console-title" class="business-console-shell__title">Business Console</h1>
      <p class="business-console-shell__summary">
        Independent business control surface for the MVP domain slices.
      </p>
    </section>

    <section class="business-console-shell__grid" aria-label="Business console MVP slices">
      <article
        v-for="slice in domainSlices"
        :key="slice.id"
        class="business-console-shell__slice"
      >
        <span class="business-console-shell__slice-name">{{ slice.label }}</span>
        <span class="business-console-shell__slice-status">{{ slice.status }}</span>
      </article>
    </section>
  </main>
</template>

<style scoped>
.business-console-shell {
  min-height: 100vh;
  padding: 48px;
  background:
    linear-gradient(120deg, color-mix(in oklch, var(--accent) 34%, transparent), transparent 46%),
    var(--background);
  color: var(--foreground);
}

.business-console-shell__header {
  max-width: 760px;
}

.business-console-shell__eyebrow {
  margin: 0 0 12px;
  color: var(--muted-foreground);
  font-size: 0.8125rem;
  font-weight: 700;
  letter-spacing: 0;
  text-transform: uppercase;
}

.business-console-shell__title {
  margin: 0;
  font-size: 2.25rem;
  font-weight: 750;
  line-height: 1.1;
}

.business-console-shell__summary {
  max-width: 540px;
  margin: 16px 0 0;
  color: var(--muted-foreground);
  font-size: 1rem;
  line-height: 1.6;
}

.business-console-shell__grid {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 16px;
  max-width: 960px;
  margin-top: 40px;
}

.business-console-shell__slice {
  min-height: 112px;
  padding: 18px;
  border: 1px solid var(--border);
  border-radius: var(--radius);
  background: var(--card);
  color: var(--card-foreground);
}

.business-console-shell__slice-name {
  display: block;
  font-size: 1rem;
  font-weight: 700;
}

.business-console-shell__slice-status {
  display: block;
  margin-top: 28px;
  color: var(--muted-foreground);
  font-size: 0.875rem;
}

@media (max-width: 760px) {
  .business-console-shell {
    padding: 28px 20px;
  }

  .business-console-shell__title {
    font-size: 1.875rem;
  }

  .business-console-shell__grid {
    grid-template-columns: 1fr;
  }
}
</style>
