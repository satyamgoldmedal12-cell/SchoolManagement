import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';
import { fileURLToPath, URL } from 'node:url';

export default defineConfig({
  plugins: [react(), tailwindcss()],
  base: '/dist/',
  build: {
    outDir: '../wwwroot/dist',
    emptyOutDir: true,
    rollupOptions: {
      input: fileURLToPath(new URL('./src/main.jsx', import.meta.url)),
      output: { entryFileNames: 'school.js', assetFileNames: 'school.[ext]' }
    }
  }
});
