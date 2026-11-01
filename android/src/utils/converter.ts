import { jsPDF } from 'jspdf';
import JSZip from 'jszip';
import { ConvertedFile, ConversionProgress, FormatType, GalleryItem, ImageOptions, PdfOptions } from '../types';

export function formatBytes(bytes: number, decimals = 1): string {
  if (bytes === 0) return '0 B';
  const k = 1024;
  const dm = decimals < 0 ? 0 : decimals;
  const sizes = ['B', 'KB', 'MB', 'GB'];
  const i = Math.floor(Math.log(bytes) / Math.log(k));
  return `${parseFloat((bytes / Math.pow(k, i)).toFixed(dm))} ${sizes[i]}`;
}

export function sanitizeFilename(name: string): string {
  return name.replace(/[^a-zA-Z0-9._-]/g, '_');
}

export function generateFilename(template: string, defaultName: string, ext: string, index?: number): string {
  const now = new Date();
  const dateStr = now.toISOString().slice(0, 10);
  const timeStr = `${String(now.getHours()).padStart(2, '0')}${String(now.getMinutes()).padStart(2, '0')}`;
  
  let res = template || 'Donut_{date}';
  res = res.replace(/\{date\}/g, dateStr);
  res = res.replace(/\{time\}/g, timeStr);
  res = res.replace(/\{name\}/g, defaultName.replace(/\.[^/.]+$/, ''));
  if (index !== undefined) {
    res = res.replace(/\{index\}/g, String(index + 1).padStart(2, '0'));
  }
  
  // if no index token was provided but index is given, append (index + 1)
  if (index !== undefined && !template.includes('{index}')) {
    res = `${res}_${String(index + 1).padStart(2, '0')}`;
  }
  
  res = sanitizeFilename(res);
  if (!res.toLowerCase().endsWith(`.${ext}`)) {
    res = `${res}.${ext}`;
  }
  return res;
}

export function loadImage(src: string): Promise<HTMLImageElement> {
  return new Promise((resolve, reject) => {
    const img = new Image();
    img.crossOrigin = 'anonymous';
    img.onload = () => resolve(img);
    img.onerror = (err) => reject(new Error('Failed to load image: ' + err));
    img.src = src;
  });
}

export function rasterizeToCanvas(img: HTMLImageElement, scale = 1.0, fillBackground = true): HTMLCanvasElement {
  const canvas = document.createElement('canvas');
  const w = Math.max(1, Math.round((img.naturalWidth || img.width || 800) * scale));
  const h = Math.max(1, Math.round((img.naturalHeight || img.height || 600) * scale));
  canvas.width = w;
  canvas.height = h;

  const ctx = canvas.getContext('2d');
  if (ctx) {
    if (fillBackground) {
      ctx.fillStyle = '#ffffff';
      ctx.fillRect(0, 0, w, h);
    }
    ctx.imageSmoothingEnabled = true;
    ctx.imageSmoothingQuality = 'high';
    ctx.drawImage(img, 0, 0, w, h);
  }
  return canvas;
}

export function canvasToBlob(canvas: HTMLCanvasElement, mimeType: string, quality = 0.85): Promise<Blob> {
  return new Promise((resolve, reject) => {
    canvas.toBlob((blob) => {
      if (blob) resolve(blob);
      else reject(new Error('Canvas toBlob failed'));
    }, mimeType, quality);
  });
}

function applyWatermark(doc: jsPDF, text: string, pW: number, pH: number) {
  if (!text || !text.trim()) return;
  try {
    doc.saveGraphicsState();
    doc.setTextColor(170, 170, 170);
    doc.setFontSize(Math.max(22, Math.min(44, Math.round(pW / 12))));
    doc.text(text.trim(), pW / 2, pH / 2, {
      align: 'center',
      baseline: 'middle',
      angle: 45
    });
    doc.restoreGraphicsState();
  } catch (e) {
    console.warn('Watermark render note:', e);
  }
}

/**
 * Core PDF Engine - Ported from Donut Convert's PdfBuilder architecture
 * Streams page-by-page to prevent memory spikes
 */
export async function convertImagesToPdf(
  items: GalleryItem[],
  options: PdfOptions,
  onProgress?: (p: ConversionProgress) => void
): Promise<ConvertedFile[]> {
  if (items.length === 0) return [];

  // Sort strictly by selectedOrder
  const sortedItems = [...items].sort((a, b) => (a.selectedOrder || 0) - (b.selectedOrder || 0));
  const total = sortedItems.length;
  const results: ConvertedFile[] = [];

  const getPageDimensions = (itemW: number, itemH: number) => {
    if (options.pageSize === 'a4') {
      return { width: 595.28, height: 841.89 }; // points (72 pt/in)
    } else if (options.pageSize === 'letter') {
      return { width: 612.0, height: 792.0 };
    } else {
      // fit or original
      return { width: itemW * 0.75, height: itemH * 0.75 };
    }
  };

  const getMarginPoints = (marginOption: string) => {
    if (marginOption === 'none') return 0;
    if (marginOption === 'narrow') return 20;
    return 36; // standard
  };

  // Adaptive quality calculation if targetSizeKb is requested
  let effectiveQuality = options.quality;
  let effectiveScale = 1.0;
  if (options.targetSizeKb && options.targetSizeKb > 0) {
    const targetKbPerPage = options.targetSizeKb / total;
    if (targetKbPerPage < 40) {
      effectiveQuality = 0.45;
      effectiveScale = 0.7;
    } else if (targetKbPerPage < 100) {
      effectiveQuality = 0.65;
      effectiveScale = 0.85;
    } else if (targetKbPerPage < 250) {
      effectiveQuality = 0.75;
    }
  }

  if (options.mode === 'single') {
    onProgress?.({
      status: 'processing',
      currentPage: 0,
      totalPages: total,
      percent: 5,
      currentFilename: 'Initializing PDF document...'
    });

    let doc: jsPDF | null = null;
    let totalOrigBytes = 0;

    for (let i = 0; i < total; i++) {
      const item = sortedItems[i];
      totalOrigBytes += item.sizeBytes;

      onProgress?.({
        status: 'processing',
        currentPage: i + 1,
        totalPages: total,
        percent: Math.round(((i + 1) / total) * 90),
        currentFilename: item.name
      });

      const img = await loadImage(item.url);
      const canvas = rasterizeToCanvas(img, effectiveScale, true);
      const dataUrl = canvas.toDataURL('image/jpeg', effectiveQuality);

      const pageDims = getPageDimensions(canvas.width, canvas.height);
      const margin = getMarginPoints(options.margins);

      let isLandscape = false;
      if (options.orientation === 'landscape') {
        isLandscape = true;
      } else if (options.orientation === 'portrait') {
        isLandscape = false;
      } else {
        // Auto
        isLandscape = canvas.width > canvas.height;
      }

      const pW = isLandscape ? Math.max(pageDims.width, pageDims.height) : Math.min(pageDims.width, pageDims.height);
      const pH = isLandscape ? Math.min(pageDims.width, pageDims.height) : Math.max(pageDims.width, pageDims.height);

      if (i === 0) {
        doc = new jsPDF({
          orientation: isLandscape ? 'landscape' : 'portrait',
          unit: 'pt',
          format: [pW, pH]
        });
      } else if (doc) {
        doc.addPage([pW, pH], isLandscape ? 'landscape' : 'portrait');
      }

      if (doc) {
        const availW = pW - margin * 2;
        const availH = pH - margin * 2;
        const imgAspect = canvas.width / canvas.height;
        const availAspect = availW / availH;

        let drawW = availW;
        let drawH = availH;
        let drawX = margin;
        let drawY = margin;

        if (imgAspect > availAspect) {
          drawW = availW;
          drawH = availW / imgAspect;
          drawY = margin + (availH - drawH) / 2;
        } else {
          drawH = availH;
          drawW = availH * imgAspect;
          drawX = margin + (availW - drawW) / 2;
        }

        doc.addImage(dataUrl, 'JPEG', drawX, drawY, drawW, drawH, undefined, 'FAST');

        // Apply watermark if requested
        if (options.watermarkText) {
          applyWatermark(doc, options.watermarkText, pW, pH);
        }
      }
    }

    if (doc) {
      const pdfBlob = doc.output('blob');
      const filename = generateFilename(options.filenamePattern, sortedItems[0]?.name || 'Document', 'pdf');
      
      const converted: ConvertedFile = {
        id: 'pdf-' + Date.now(),
        filename,
        format: 'pdf',
        blob: pdfBlob,
        downloadUrl: URL.createObjectURL(pdfBlob),
        originalSizeBytes: totalOrigBytes,
        convertedSizeBytes: pdfBlob.size,
        pageCount: total,
        timestamp: Date.now(),
        previewUrl: sortedItems[0]?.url
      };

      results.push(converted);
    }
  } else {
    // Separate individual PDFs for each selected item
    for (let i = 0; i < total; i++) {
      const item = sortedItems[i];
      onProgress?.({
        status: 'processing',
        currentPage: i + 1,
        totalPages: total,
        percent: Math.round(((i + 1) / total) * 90),
        currentFilename: item.name
      });

      const img = await loadImage(item.url);
      const canvas = rasterizeToCanvas(img, effectiveScale, true);
      const dataUrl = canvas.toDataURL('image/jpeg', effectiveQuality);

      const pageDims = getPageDimensions(canvas.width, canvas.height);
      const margin = getMarginPoints(options.margins);
      const isLandscape = options.orientation === 'landscape' ? true :
        options.orientation === 'portrait' ? false :
        canvas.width > canvas.height;

      const pW = isLandscape ? Math.max(pageDims.width, pageDims.height) : Math.min(pageDims.width, pageDims.height);
      const pH = isLandscape ? Math.min(pageDims.width, pageDims.height) : Math.max(pageDims.width, pageDims.height);

      const doc = new jsPDF({
        orientation: isLandscape ? 'landscape' : 'portrait',
        unit: 'pt',
        format: [pW, pH]
      });

      const availW = pW - margin * 2;
      const availH = pH - margin * 2;
      const imgAspect = canvas.width / canvas.height;
      const availAspect = availW / availH;

      let drawW = availW;
      let drawH = availH;
      let drawX = margin;
      let drawY = margin;

      if (imgAspect > availAspect) {
        drawW = availW;
        drawH = availW / imgAspect;
        drawY = margin + (availH - drawH) / 2;
      } else {
        drawH = availH;
        drawW = availH * imgAspect;
        drawX = margin + (availW - drawW) / 2;
      }

      doc.addImage(dataUrl, 'JPEG', drawX, drawY, drawW, drawH, undefined, 'FAST');

      if (options.watermarkText) {
        applyWatermark(doc, options.watermarkText, pW, pH);
      }

      const pdfBlob = doc.output('blob');
      const filename = generateFilename(options.filenamePattern, item.name, 'pdf', i);

      results.push({
        id: `pdf-sep-${i}-${Date.now()}`,
        filename,
        format: 'pdf',
        blob: pdfBlob,
        downloadUrl: URL.createObjectURL(pdfBlob),
        originalSizeBytes: item.sizeBytes,
        convertedSizeBytes: pdfBlob.size,
        pageCount: 1,
        timestamp: Date.now(),
        previewUrl: item.url
      });
    }
  }

  onProgress?.({
    status: 'completed',
    currentPage: total,
    totalPages: total,
    percent: 100
  });

  return results;
}

/**
 * Image Converter Engine - Converts to JPG, PNG, WEBP with high-performance canvas
 */
export async function convertImagesToImageFormat(
  items: GalleryItem[],
  options: ImageOptions,
  onProgress?: (p: ConversionProgress) => void
): Promise<ConvertedFile[]> {
  if (items.length === 0) return [];

  const sortedItems = [...items].sort((a, b) => (a.selectedOrder || 0) - (b.selectedOrder || 0));
  const total = sortedItems.length;
  const mimeMap: Record<string, string> = {
    jpg: 'image/jpeg',
    png: 'image/png',
    webp: 'image/webp'
  };
  const mime = mimeMap[options.format] || 'image/jpeg';
  const results: ConvertedFile[] = [];

  for (let i = 0; i < total; i++) {
    const item = sortedItems[i];
    onProgress?.({
      status: 'processing',
      currentPage: i + 1,
      totalPages: total,
      percent: Math.round(((i + 1) / total) * 90),
      currentFilename: item.name
    });

    const img = await loadImage(item.url);
    let scale = options.scale;
    let quality = options.quality;
    let canvas = rasterizeToCanvas(img, scale, options.format === 'jpg');
    let blob = await canvasToBlob(canvas, mime, quality);

    // Adaptive compression loop if target size is requested (e.g. Under 100 KB or 200 KB)
    if (options.targetSizeKb && options.targetSizeKb > 0) {
      const targetBytes = options.targetSizeKb * 1024;
      let step = 0;
      while (blob.size > targetBytes && step < 4 && (quality > 0.15 || scale > 0.4)) {
        step++;
        if (options.format !== 'png') {
          quality = Math.max(0.12, quality * 0.75);
        }
        if (step >= 2) {
          scale = Math.max(0.35, scale * 0.8);
          canvas = rasterizeToCanvas(img, scale, options.format === 'jpg');
        }
        blob = await canvasToBlob(canvas, mime, quality);
      }
    }

    const filename = generateFilename(
      options.filenamePattern,
      item.name,
      options.format,
      total > 1 ? i : undefined
    );

    const converted: ConvertedFile = {
      id: `img-${options.format}-${i}-${Date.now()}`,
      filename,
      format: options.format,
      blob,
      downloadUrl: URL.createObjectURL(blob),
      originalSizeBytes: item.sizeBytes,
      convertedSizeBytes: blob.size,
      timestamp: Date.now(),
      previewUrl: URL.createObjectURL(blob)
    };

    results.push(converted);
  }

  // If user requested zip packaging or multiple files
  if (options.packageAsZip && results.length > 1) {
    onProgress?.({
      status: 'processing',
      currentPage: total,
      totalPages: total,
      percent: 95,
      currentFilename: 'Packaging into ZIP archive...'
    });

    const zip = new JSZip();
    results.forEach(file => {
      zip.file(file.filename, file.blob);
    });

    const zipBlob = await zip.generateAsync({ type: 'blob', compression: 'DEFLATE' });
    const zipName = `Donut_Batch_${options.format.toUpperCase()}_${Date.now()}.zip`;

    const zipFile: ConvertedFile = {
      id: `zip-${Date.now()}`,
      filename: zipName,
      format: options.format,
      blob: zipBlob,
      downloadUrl: URL.createObjectURL(zipBlob),
      originalSizeBytes: results.reduce((acc, f) => acc + f.originalSizeBytes, 0),
      convertedSizeBytes: zipBlob.size,
      pageCount: results.length,
      timestamp: Date.now(),
      previewUrl: results[0]?.previewUrl
    };

    results.unshift(zipFile);
  }

  onProgress?.({
    status: 'completed',
    currentPage: total,
    totalPages: total,
    percent: 100
  });

  return results;
}

/**
 * Downloads a single converted file safely
 */
export function downloadFile(file: ConvertedFile) {
  const link = document.createElement('a');
  link.href = file.downloadUrl;
  link.download = file.filename;
  document.body.appendChild(link);
  link.click();
  document.body.removeChild(link);
}

/**
 * Android 1-Tap Share Sheet (WhatsApp, Gmail, Drive, etc.) via Web Share API
 */
export async function shareConvertedFile(file: ConvertedFile): Promise<boolean> {
  if (typeof navigator !== 'undefined' && navigator.share) {
    try {
      const mimeType = file.blob.type || (file.format === 'pdf' ? 'application/pdf' : `image/${file.format}`);
      const shareFileObj = new File([file.blob], file.filename, { type: mimeType });

      if (navigator.canShare && navigator.canShare({ files: [shareFileObj] })) {
        await navigator.share({
          files: [shareFileObj],
          title: file.filename,
          text: `Converted with Donut Convert: ${file.filename}`
        });
        return true;
      } else {
        await navigator.share({
          title: file.filename,
          text: `Download converted file: ${file.filename}`,
          url: file.downloadUrl
        });
        return true;
      }
    } catch (err) {
      if ((err as Error).name !== 'AbortError') {
        console.warn('Native share error:', err);
      }
      return false;
    }
  }
  return false;
}
