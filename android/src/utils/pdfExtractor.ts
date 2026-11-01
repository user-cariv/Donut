import * as pdfjsLib from 'pdfjs-dist';
import pdfjsWorker from 'pdfjs-dist/build/pdf.worker.min.mjs?url';
import JSZip from 'jszip';
import { ConvertedFile, GalleryItem } from '../types';

// Set up PDF.js worker using Vite's URL asset loading
pdfjsLib.GlobalWorkerOptions.workerSrc = pdfjsWorker;

export async function extractPagesFromPdf(
  file: File,
  scale = 2.0,
  onProgress?: (current: number, total: number) => void
): Promise<GalleryItem[]> {
  const arrayBuffer = await file.arrayBuffer();
  const loadingTask = pdfjsLib.getDocument({ data: arrayBuffer });
  const pdfDoc = await loadingTask.promise;
  const numPages = pdfDoc.numPages;

  const baseName = file.name.replace(/\.[^/.]+$/, '');
  const items: GalleryItem[] = [];

  for (let pageNum = 1; pageNum <= numPages; pageNum++) {
    onProgress?.(pageNum, numPages);
    const page = await pdfDoc.getPage(pageNum);
    const viewport = page.getViewport({ scale });

    const canvas = document.createElement('canvas');
    canvas.width = viewport.width;
    canvas.height = viewport.height;
    const ctx = canvas.getContext('2d');

    if (!ctx) continue;

    // Fill white background for non-alpha rendering
    ctx.fillStyle = '#ffffff';
    ctx.fillRect(0, 0, canvas.width, canvas.height);

    await page.render({
      canvasContext: ctx,
      viewport,
      canvas: canvas
    } as any).promise;

    const dataUrl = canvas.toDataURL('image/png');
    // Calculate approximate size in bytes from base64 length
    const approxBytes = Math.round((dataUrl.length * 3) / 4);

    items.push({
      id: `pdf-page-${Date.now()}-${pageNum}-${Math.random().toString(36).slice(2, 6)}`,
      name: `${baseName}_Page_${String(pageNum).padStart(2, '0')}.png`,
      url: dataUrl,
      type: 'image/png',
      sizeBytes: approxBytes,
      width: Math.round(viewport.width),
      height: Math.round(viewport.height),
      dateAdded: 'Just now',
      category: 'document',
      selectedOrder: pageNum
    });
  }

  return items;
}

export async function convertPdfToImages(
  file: File,
  format: 'jpg' | 'png' | 'webp',
  quality = 0.9,
  onProgress?: (current: number, total: number) => void
): Promise<ConvertedFile[]> {
  const arrayBuffer = await file.arrayBuffer();
  const loadingTask = pdfjsLib.getDocument({ data: arrayBuffer });
  const pdfDoc = await loadingTask.promise;
  const numPages = pdfDoc.numPages;

  const mimeMap = {
    jpg: 'image/jpeg',
    png: 'image/png',
    webp: 'image/webp'
  };
  const mime = mimeMap[format] || 'image/jpeg';
  const baseName = file.name.replace(/\.[^/.]+$/, '');
  const results: ConvertedFile[] = [];

  for (let pageNum = 1; pageNum <= numPages; pageNum++) {
    onProgress?.(pageNum, numPages);
    const page = await pdfDoc.getPage(pageNum);
    const viewport = page.getViewport({ scale: 2.0 });

    const canvas = document.createElement('canvas');
    canvas.width = viewport.width;
    canvas.height = viewport.height;
    const ctx = canvas.getContext('2d');

    if (ctx) {
      if (format === 'jpg') {
        ctx.fillStyle = '#ffffff';
        ctx.fillRect(0, 0, canvas.width, canvas.height);
      }
      await page.render({ canvasContext: ctx, viewport, canvas: canvas } as any).promise;

      const blob = await new Promise<Blob>((resolve, reject) => {
        canvas.toBlob(
          (b) => {
            if (b) resolve(b);
            else reject(new Error('Canvas conversion to blob failed'));
          },
          mime,
          quality
        );
      });

      const fileName = `${baseName}_Page_${String(pageNum).padStart(2, '0')}.${format}`;
      const url = URL.createObjectURL(blob);

      results.push({
        id: `pdf-conv-${Date.now()}-${pageNum}`,
        filename: fileName,
        format,
        blob,
        downloadUrl: url,
        originalSizeBytes: Math.round(file.size / numPages),
        convertedSizeBytes: blob.size,
        timestamp: Date.now(),
        previewUrl: url
      });
    }
  }

  // If more than 1 page, also generate a single zip
  if (results.length > 1) {
    const zip = new JSZip();
    results.forEach((res) => {
      zip.file(res.filename, res.blob);
    });

    const zipBlob = await zip.generateAsync({ type: 'blob', compression: 'DEFLATE' });
    const zipName = `${baseName}_All_Pages_${format.toUpperCase()}.zip`;
    const zipUrl = URL.createObjectURL(zipBlob);

    results.unshift({
      id: `zip-${Date.now()}`,
      filename: zipName,
      format,
      blob: zipBlob,
      downloadUrl: zipUrl,
      originalSizeBytes: file.size,
      convertedSizeBytes: zipBlob.size,
      pageCount: results.length,
      timestamp: Date.now(),
      previewUrl: results[0]?.previewUrl
    });
  }

  return results;
}
