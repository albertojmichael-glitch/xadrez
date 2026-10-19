import { defineConfig } from 'vite';

export default defineConfig({
  server: {
    allowedHosts: true,
  },
  preview: {
    allowedHosts: true,
  },
});