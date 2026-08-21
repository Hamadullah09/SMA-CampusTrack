import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import path from 'node:path';

export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: { '@': path.resolve(__dirname, './src') },
  },
  server: {
    port: 5173,
    // The API is proxied in development so the browser sees one origin: no CORS
    // preflight on every call, and cookies/headers behave as they will in production
    // where both are served from the same host.
    proxy: {
      '/api': { target: 'http://localhost:5080', changeOrigin: true },
      '/hubs': { target: 'http://localhost:5080', changeOrigin: true, ws: true },
    },
  },
  build: {
    outDir: 'dist',
    sourcemap: true,
    rollupOptions: {
      output: {
        // Charting and the realtime client are large and change rarely; splitting them
        // keeps the main bundle small on the login screen where neither is needed.
        manualChunks: {
          charts: ['recharts'],
          realtime: ['@microsoft/signalr'],
        },
      },
    },
  },
});
