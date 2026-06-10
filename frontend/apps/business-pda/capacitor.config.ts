import type { CapacitorConfig } from '@capacitor/cli'

const config: CapacitorConfig = {
  appId: 'com.nerviip.pda',
  appName: 'Nerv-IIP 手持作业台',
  webDir: 'dist',
  server: {
    androidScheme: 'https',
  },
}

export default config
