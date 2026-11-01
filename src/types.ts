export type FormatType = 'pdf' | 'jpg' | 'png' | 'webp';

export type PageSize = 'a4' | 'letter' | 'fit' | 'original';
export type PageOrientation = 'auto' | 'portrait' | 'landscape';
export type MarginOption = 'none' | 'narrow' | 'standard';

export interface GalleryItem {
  id: string;
  name: string;
  url: string;
  type: string;
  sizeBytes: number;
  width: number;
  height: number;
  dateAdded: string;
  category: 'document' | 'receipt' | 'photo' | 'note' | 'custom';
  selectedOrder?: number; // 1-indexed tap order for page sequencing
}

export interface PdfOptions {
  mode: 'single' | 'separate'; // One PDF vs Separate PDFs
  pageSize: PageSize;
  orientation: PageOrientation;
  margins: MarginOption;
  quality: number; // 0.1 to 1.0 (default 0.85)
  filenamePattern: string; // e.g. "Donut_{date}"
  rememberChoice: boolean;
  targetSizeKb?: number; // e.g. 100, 200, 500 KB limit
  watermarkText?: string; // Optional watermark text
  password?: string; // Optional password
}

export interface ImageOptions {
  quality: number; // 0.1 to 1.0
  scale: number; // 0.5, 0.75, 1.0, 1.5, 2.0
  format: 'jpg' | 'png' | 'webp';
  filenamePattern: string;
  rememberChoice: boolean;
  packageAsZip: boolean;
  targetSizeKb?: number; // Optional target size limit per image in KB
}

export interface ConvertedFile {
  id: string;
  filename: string;
  format: FormatType;
  blob: Blob;
  downloadUrl: string;
  originalSizeBytes: number;
  convertedSizeBytes: number;
  pageCount?: number;
  timestamp: number;
  previewUrl?: string;
}

export interface ConversionProgress {
  status: 'idle' | 'processing' | 'completed' | 'error';
  currentPage: number;
  totalPages: number;
  percent: number;
  currentFilename?: string;
  error?: string;
}
