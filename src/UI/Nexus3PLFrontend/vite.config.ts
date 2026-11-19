import { defineConfig } from 'vitest/config';

export default defineConfig({
  server: {
    port: 3001,
    strictPort: true,
    proxy: {
      '/api': {
        target: 'http://127.0.0.1:5000',
        changeOrigin: true
      }
    }
  },
  test: {
    environment: 'jsdom',
    restoreMocks: true,
    globals: true,
    exclude: ['e2e/**', 'node_modules/**', 'dist/**']
  }
});
