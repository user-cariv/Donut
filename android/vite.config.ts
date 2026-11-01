import tailwindcss from '@tailwindcss/vite';
import react from '@vitejs/plugin-react';
import fs from 'fs';
import path from 'path';
import {defineConfig, Plugin} from 'vite';

function downloadFilePlugin(): Plugin {
  return {
    name: 'download-file-plugin',
    configureServer(server) {
      server.middlewares.use((req, res, next) => {
        const cleanUrl = req.url ? req.url.split('?')[0] : '';
        const decodedUrl = decodeURIComponent(cleanUrl);
        if (
          cleanUrl === '/Donut_Convert.apk' ||
          cleanUrl === '/DonutConvert.apk' ||
          decodedUrl === '/Donut Convert.apk' ||
          cleanUrl === '/donut_convert.apk'
        ) {
          const filePath = path.resolve(__dirname, 'public/Donut_Convert.apk');
          if (fs.existsSync(filePath)) {
            const stat = fs.statSync(filePath);
            res.writeHead(200, {
              'Content-Type': 'application/vnd.android.package-archive',
              'Content-Disposition': 'attachment; filename="Donut_Convert.apk"',
              'Content-Length': stat.size,
              'Access-Control-Allow-Origin': '*',
            });
            fs.createReadStream(filePath).pipe(res);
            return;
          }
        }
        if (cleanUrl === '/Donut-Convert-Full-Project.zip') {
          const filePath = path.resolve(__dirname, 'public/Donut-Convert-Full-Project.zip');
          if (fs.existsSync(filePath)) {
            const stat = fs.statSync(filePath);
            res.writeHead(200, {
              'Content-Type': 'application/zip',
              'Content-Disposition': 'attachment; filename="Donut-Convert-Full-Project.zip"',
              'Content-Length': stat.size,
              'Access-Control-Allow-Origin': '*',
            });
            fs.createReadStream(filePath).pipe(res);
            return;
          }
        }
        next();
      });
    },
  };
}

export default defineConfig(() => {
  return {
    base: './',
    plugins: [react(), tailwindcss(), downloadFilePlugin()],
    resolve: {
      alias: {
        '@': path.resolve(__dirname, '.'),
      },
    },
    server: {
      // HMR is disabled in AI Studio via DISABLE_HMR env var.
      // Do not modify—file watching is disabled to prevent flickering during agent edits.
      hmr: process.env.DISABLE_HMR !== 'true',
      // Disable file watching when DISABLE_HMR is true to save CPU during agent edits.
      watch: process.env.DISABLE_HMR === 'true' ? null : {},
    },
  };
});
