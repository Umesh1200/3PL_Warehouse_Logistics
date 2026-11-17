import { defineConfig } from 'vitest/config';
export default defineConfig({
  server: { port: 3000, strictPort: true, proxy: { '/api': 'http://localhost:5000' } },
  test: { environment: 'jsdom', restoreMocks: true }
});
