import React, { useState, useEffect, useCallback, useRef } from 'react';
import {
  Sparkles,
  Smartphone,
  FolderDown,
  Layers,
  Settings,
  Plus,
  HelpCircle,
  FileCheck2,
  Share2,
  Copy,
  Zap,
  Info,
  ExternalLink,
  Code2,
  FileText,
  Image as ImageIcon,
  Upload,
  CheckCircle2,
  Check,
  QrCode
} from 'lucide-react';
import { SAMPLE_GALLERY_ITEMS } from './utils/sampleData';
import {
  ConvertedFile,
  ConversionProgress,
  FormatType,
  GalleryItem,
  ImageOptions,
  PdfOptions
} from './types';
import { RadialPicker } from './components/RadialPicker';
import { GalleryGrid } from './components/GalleryGrid';
import { PdfOptionsSheet } from './components/PdfOptionsSheet';
import { ImageOptionsSheet } from './components/ImageOptionsSheet';
import { ConversionModal } from './components/ConversionModal';
import { ConvertedHistory } from './components/ConvertedHistory';
import { FloatingBubble } from './components/FloatingBubble';
import { CameraScannerModal } from './components/CameraScannerModal';
import { ApkDownloadModal } from './components/ApkDownloadModal';
import {
  convertImagesToPdf,
  convertImagesToImageFormat,
  downloadFile
} from './utils/converter';
import { playSectorSelect, playHapticTick, playSuccessChime } from './utils/audio';

import { extractPagesFromPdf } from './utils/pdfExtractor';

const WEBSITE_URL = 'https://user-cariv.github.io/Donut-Convert/';

export default function App() {
  // Empty initial gallery as requested: user adds their own photos or PDFs
  const [galleryItems, setGalleryItems] = useState<GalleryItem[]>([]);
  const [selectedIds, setSelectedIds] = useState<string[]>([]);

  const [convertedFiles, setConvertedFiles] = useState<ConvertedFile[]>([]);
  const [activeTab, setActiveTab] = useState<'gallery' | 'history'>('gallery');
  const [showFloatingBubble, setShowFloatingBubble] = useState<boolean>(true);
  const [showApkGuideModal, setShowApkGuideModal] = useState<boolean>(false);
  const [showApkDownloadModal, setShowApkDownloadModal] = useState<boolean>(false);
  const [showScannerModal, setShowScannerModal] = useState<boolean>(false);
  const [copyApkCmdSuccess, setCopyApkCmdSuccess] = useState<boolean>(false);
  const [isProcessingPdf, setIsProcessingPdf] = useState<boolean>(false);
  const [showApkBanner, setShowApkBanner] = useState<boolean>(true);

  // Conversion Modals & Progress
  const [activeFormat, setActiveFormat] = useState<FormatType>('pdf');
  const [showPdfSheet, setShowPdfSheet] = useState<boolean>(false);
  const [showImageSheet, setShowImageSheet] = useState<boolean>(false);
  const [showConversionModal, setShowConversionModal] = useState<boolean>(false);
  const [conversionProgress, setConversionProgress] = useState<ConversionProgress>({
    status: 'idle',
    currentPage: 0,
    totalPages: 0,
    percent: 0
  });
  const [currentJobResults, setCurrentJobResults] = useState<ConvertedFile[]>([]);

  const fileInputRef = useRef<HTMLInputElement>(null);

  // Selected items sorted by sequence order (tap order = page order)
  const selectedItems = galleryItems
    .filter((it) => selectedIds.includes(it.id))
    .sort((a, b) => (a.selectedOrder || 0) - (b.selectedOrder || 0));

  // Toggle item selection (Numbered Multi-Select, tap order = page order)
  const handleToggleSelectItem = useCallback((item: GalleryItem) => {
    setGalleryItems((prev) => {
      const isAlreadySelected = prev.some((it) => it.id === item.id && (it.selectedOrder ?? 0) > 0);

      if (isAlreadySelected) {
        // Deselect and re-index remaining selected items
        const remaining = prev
          .filter((it) => it.id !== item.id && (it.selectedOrder ?? 0) > 0)
          .sort((a, b) => (a.selectedOrder || 0) - (b.selectedOrder || 0));

        const orderMap = new Map<string, number>();
        remaining.forEach((it, idx) => {
          orderMap.set(it.id, idx + 1);
        });

        setSelectedIds(remaining.map((it) => it.id));

        return prev.map((it) => {
          if (it.id === item.id) {
            return { ...it, selectedOrder: undefined };
          }
          if (orderMap.has(it.id)) {
            return { ...it, selectedOrder: orderMap.get(it.id) };
          }
          return it;
        });
      } else {
        // Select with next highest sequence number
        const currentHighest = prev.reduce((max, it) => Math.max(max, it.selectedOrder || 0), 0);
        const nextOrder = currentHighest + 1;

        setSelectedIds((ids) => [...ids, item.id]);

        return prev.map((it) => {
          if (it.id === item.id) {
            return { ...it, selectedOrder: nextOrder };
          }
          return it;
        });
      }
    });
  }, []);

  const handleClearSelection = useCallback(() => {
    setSelectedIds([]);
    setGalleryItems((prev) => prev.map((it) => ({ ...it, selectedOrder: undefined })));
  }, []);

  const handleSelectAll = useCallback(() => {
    setGalleryItems((prev) => {
      const newItems = prev.map((it, idx) => ({ ...it, selectedOrder: idx + 1 }));
      setSelectedIds(newItems.map((it) => it.id));
      return newItems;
    });
  }, []);

  const handleReorderItems = useCallback((newOrdered: GalleryItem[]) => {
    const orderMap = new Map<string, number>();
    newOrdered.forEach((it, idx) => {
      orderMap.set(it.id, idx + 1);
    });

    setGalleryItems((prev) =>
      prev.map((it) => {
        if (orderMap.has(it.id)) {
          return { ...it, selectedOrder: orderMap.get(it.id) };
        }
        return it;
      })
    );
  }, []);

  const handleDeleteItem = useCallback((id: string) => {
    setGalleryItems((prev) => {
      const remaining = prev.filter((it) => it.id !== id);
      const selected = remaining
        .filter((it) => (it.selectedOrder ?? 0) > 0)
        .sort((a, b) => (a.selectedOrder || 0) - (b.selectedOrder || 0));

      const orderMap = new Map<string, number>();
      selected.forEach((it, idx) => {
        orderMap.set(it.id, idx + 1);
      });

      setSelectedIds(selected.map((it) => it.id));

      return remaining.map((it) => {
        if (orderMap.has(it.id)) {
          return { ...it, selectedOrder: orderMap.get(it.id) };
        }
        return { ...it, selectedOrder: undefined };
      });
    });
  }, []);

  const [workflowToast, setWorkflowToast] = useState<string | null>(null);

  // Handle uploading both images and PDF documents!
  const handleUploadFiles = useCallback(async (files: FileList) => {
    const fileArray = Array.from(files);
    if (fileArray.length === 0) return;

    const newItems: GalleryItem[] = [];
    setIsProcessingPdf(true);

    for (const file of fileArray) {
      if (file.type === 'application/pdf' || file.name.toLowerCase().endsWith('.pdf')) {
        // PDF uploaded: Extract pages into high-DPI images for JPG/PNG conversion!
        setWorkflowToast(`Extracting pages from PDF: ${file.name}...`);
        try {
          const pages = await extractPagesFromPdf(file, 2.0);
          newItems.push(...pages);
        } catch (err) {
          console.error('Failed to extract PDF:', err);
          setWorkflowToast(`Error reading PDF: ${String(err)}`);
        }
      } else {
        // Regular image file
        await new Promise<void>((resolve) => {
          const reader = new FileReader();
          reader.onload = (e) => {
            const url = e.target?.result as string;
            const img = new Image();
            img.onload = () => {
              newItems.push({
                id: 'upload-' + Date.now() + '-' + Math.random().toString(36).slice(2, 6),
                name: file.name,
                url: url,
                type: file.type || 'image/jpeg',
                sizeBytes: file.size,
                width: img.naturalWidth || 1200,
                height: img.naturalHeight || 800,
                dateAdded: 'Just now',
                category: 'custom'
              });
              resolve();
            };
            img.onerror = () => resolve();
            img.src = url;
          };
          reader.onerror = () => resolve();
          reader.readAsDataURL(file);
        });
      }
    }

    setIsProcessingPdf(false);

    if (newItems.length > 0) {
      setGalleryItems((prev) => {
        const highestOrder = prev.reduce((max, it) => Math.max(max, it.selectedOrder || 0), 0);
        const queuedNewItems = newItems.map((item, idx) => ({
          ...item,
          selectedOrder: highestOrder + idx + 1
        }));

        setSelectedIds((prevIds) => [...prevIds, ...queuedNewItems.map((it) => it.id)]);
        return [...queuedNewItems, ...prev];
      });

      const isPdfUpload = fileArray.some(
        (f) => f.type === 'application/pdf' || f.name.toLowerCase().endsWith('.pdf')
      );

      if (isPdfUpload) {
        setWorkflowToast(
          `✓ PDF pages extracted! You can now convert them to JPG, PNG, or WebP below.`
        );
      } else {
        setWorkflowToast(
          `✓ ${newItems.length} photo(s) added & queued! Next: Tap 'Convert to PDF' or pick a format below.`
        );
      }
      playSuccessChime();
    }
  }, []);

  const handleAddScannedItems = useCallback((items: GalleryItem[]) => {
    setGalleryItems((prev) => {
      const highestOrder = prev.reduce((max, it) => Math.max(max, it.selectedOrder || 0), 0);
      const queuedNewItems = items.map((item, idx) => ({
        ...item,
        selectedOrder: highestOrder + idx + 1
      }));
      setSelectedIds((prevIds) => [...prevIds, ...queuedNewItems.map((it) => it.id)]);
      return [...queuedNewItems, ...prev];
    });
    setWorkflowToast(`✓ ${items.length} document page(s) scanned & queued! Next: Pick a format below.`);
    playSuccessChime();
  }, []);

  // Clipboard paste listener (Quick Convert feature)
  useEffect(() => {
    const handlePaste = (e: ClipboardEvent) => {
      if (e.clipboardData?.files && e.clipboardData.files.length > 0) {
        handleUploadFiles(e.clipboardData.files);
      }
    };
    window.addEventListener('paste', handlePaste);
    return () => window.removeEventListener('paste', handlePaste);
  }, [handleUploadFiles]);

  // Format selection from Radial Donut Picker
  const handleSelectFormat = (format: FormatType) => {
    setActiveFormat(format);

    // If 0 items selected, automatically select all photos so user is never locked out!
    let itemsToProcess = selectedItems;
    if (itemsToProcess.length === 0 && galleryItems.length > 0) {
      const allItems = galleryItems.map((it, idx) => ({ ...it, selectedOrder: idx + 1 }));
      setGalleryItems(allItems);
      setSelectedIds(allItems.map((it) => it.id));
    }

    if (format === 'pdf') {
      setShowPdfSheet(true);
    } else {
      setShowImageSheet(true);
    }
  };

  // Instant 1-Click conversion without options modal
  const handleInstantConvert = async (format: FormatType) => {
    playSectorSelect();
    let itemsToProcess = selectedItems;
    if (itemsToProcess.length === 0) {
      const allItems = galleryItems.map((it, idx) => ({ ...it, selectedOrder: idx + 1 }));
      setGalleryItems(allItems);
      setSelectedIds(allItems.map((it) => it.id));
      itemsToProcess = allItems;
    }

    if (format === 'pdf') {
      handleStartPdfConversion({
        mode: 'single',
        pageSize: 'a4',
        orientation: 'auto',
        margins: 'none',
        quality: 0.85,
        filenamePattern: 'Donut_Doc_{date}',
        rememberChoice: true
      });
    } else {
      handleStartImageConversion({
        format,
        quality: format === 'png' ? 1.0 : 0.85,
        scale: 1.0,
        filenamePattern: `Donut_${format.toUpperCase()}_{date}`,
        packageAsZip: itemsToProcess.length > 1,
        rememberChoice: true
      });
    }
  };

  // Execute PDF conversion
  const handleStartPdfConversion = async (options: PdfOptions) => {
    setShowPdfSheet(false);
    setShowConversionModal(true);
    setConversionProgress({
      status: 'processing',
      currentPage: 0,
      totalPages: selectedItems.length,
      percent: 5
    });

    try {
      const results = await convertImagesToPdf(selectedItems, options, (p) => {
        setConversionProgress(p);
      });

      setCurrentJobResults(results);
      setConvertedFiles((prev) => [...results, ...prev]);
    } catch (err) {
      setConversionProgress({
        status: 'error',
        currentPage: 0,
        totalPages: selectedItems.length,
        percent: 0,
        error: String(err)
      });
    }
  };

  // Execute Image conversion (JPG, PNG, WEBP)
  const handleStartImageConversion = async (options: ImageOptions) => {
    setShowImageSheet(false);
    setShowConversionModal(true);
    setConversionProgress({
      status: 'processing',
      currentPage: 0,
      totalPages: selectedItems.length,
      percent: 5
    });

    try {
      const results = await convertImagesToImageFormat(selectedItems, options, (p) => {
        setConversionProgress(p);
      });

      setCurrentJobResults(results);
      setConvertedFiles((prev) => [...results, ...prev]);
    } catch (err) {
      setConversionProgress({
        status: 'error',
        currentPage: 0,
        totalPages: selectedItems.length,
        percent: 0,
        error: String(err)
      });
    }
  };

  const handleCopyApkCommand = (cmd: string) => {
    navigator.clipboard.writeText(cmd).then(() => {
      setCopyApkCmdSuccess(true);
      setTimeout(() => setCopyApkCmdSuccess(false), 2000);
    });
  };

  const handleDownloadApk = async () => {
    setShowApkDownloadModal(true);
    try {
      const res = await fetch('./Donut_Convert.apk');
      if (res.ok) {
        const blob = await res.blob();
        const blobUrl = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.style.display = 'none';
        a.href = blobUrl;
        a.download = 'Donut_Convert.apk';
        document.body.appendChild(a);
        a.click();
        setTimeout(() => {
          document.body.removeChild(a);
          URL.revokeObjectURL(blobUrl);
        }, 3000);
        setWorkflowToast('✓ Donut_Convert.apk download started! Look in your Downloads folder.');
        playSuccessChime();
      }
    } catch (err) {
      console.warn('Iframe download handled via modal:', err);
    }
  };

  return (
    <div className="min-h-screen bg-stone-900 text-stone-100 flex flex-col font-sans selection:bg-amber-500 selection:text-stone-950">
      {/* Prominent Website Link Banner requested by user */}
      <div className="bg-gradient-to-r from-amber-600/20 via-orange-600/20 to-amber-600/20 border-b border-amber-500/30 px-4 py-2 flex flex-wrap items-center justify-between gap-3 text-xs">
        <div className="flex items-center gap-2">
          <div className="w-5 h-5 rounded-full bg-amber-500 text-stone-950 flex items-center justify-center font-bold text-[10px]">
            ★
          </div>
          <span className="text-stone-200">
            Official website created for Mac &amp; Windows:{' '}
            <strong className="text-amber-400 font-mono">user-cariv.github.io/Donut-Convert</strong>
          </span>
        </div>

        <div className="flex items-center gap-2">
          <a
            href={WEBSITE_URL}
            target="_blank"
            rel="noopener noreferrer"
            className="px-3.5 py-1 rounded-full bg-amber-500 hover:bg-amber-400 text-stone-950 font-bold transition-all flex items-center gap-1.5 shadow-md shadow-amber-500/20 active:scale-95"
          >
            <span>Open Website</span>
            <ExternalLink size={12} strokeWidth={2.5} />
          </a>
        </div>
      </div>

      {/* Top Bar Contract: Zone 1 (Wordmark) — Zone 2 (4 Links) — Zone 3 (1 Action) */}
      <header className="flex items-center justify-between gap-8 px-6 py-3.5 border-b border-stone-800 bg-stone-950/90 backdrop-blur-md sticky top-0 z-40">
        {/* Zone 1: Wordmark */}
        <div className="flex items-center gap-2.5">
          <div className="w-8 h-8 rounded-full border-2 border-amber-500 flex items-center justify-center bg-stone-900 shadow-sm shrink-0">
            <div className="w-2.5 h-2.5 rounded-full bg-amber-500" />
          </div>
          <span className="text-lg font-bold tracking-tight font-display text-white whitespace-nowrap shrink-0">
            Donut Convert
          </span>
        </div>

        {/* Zone 2: Navigation Links */}
        <nav className="hidden md:flex items-center gap-6 text-xs font-semibold text-stone-400">
          <button
            onClick={() => setActiveTab('gallery')}
            className={`hover:text-white transition-colors whitespace-nowrap shrink-0 cursor-pointer ${
              activeTab === 'gallery' ? 'text-amber-400 font-bold' : ''
            }`}
          >
            Converter Studio
          </button>
          <button
            onClick={() => setActiveTab('history')}
            className={`hover:text-white transition-colors whitespace-nowrap shrink-0 cursor-pointer ${
              activeTab === 'history' ? 'text-amber-400 font-bold' : ''
            }`}
          >
            Saved Files ({convertedFiles.length})
          </button>
          <button
            onClick={() => setShowApkGuideModal(true)}
            className="hover:text-amber-400 transition-colors whitespace-nowrap shrink-0 flex items-center gap-1 text-stone-300 cursor-pointer"
          >
            <Smartphone size={14} className="text-amber-400" />
            <span>Play Store Setup</span>
          </button>
        </nav>

        {/* Zone 3: Quick Action */}
        <div className="flex items-center gap-2 shrink-0">
          <button
            onClick={handleDownloadApk}
            className="px-3.5 py-2 text-xs font-bold text-stone-950 bg-amber-500 hover:bg-amber-400 rounded-xl transition-all shadow-md shadow-amber-500/20 whitespace-nowrap shrink-0 flex items-center gap-1.5 cursor-pointer active:scale-95"
            title="Download Donut Convert Android APK directly without redirects"
          >
            <Smartphone size={14} className="text-stone-950" />
            <span>Download APK</span>
          </button>
          <button
            onClick={() => setShowApkDownloadModal(true)}
            className="hidden sm:flex px-3 py-2 text-xs font-semibold text-stone-300 bg-stone-900 hover:bg-stone-800 rounded-xl transition-colors whitespace-nowrap shrink-0 items-center gap-1.5 border border-stone-800 cursor-pointer"
            title="Scan Phone QR Code or view mirrors"
          >
            <QrCode size={13} className="text-amber-400" />
            <span>Phone QR</span>
          </button>
          <a
            href={WEBSITE_URL}
            target="_blank"
            rel="noopener noreferrer"
            className="px-3.5 py-2 text-xs font-bold text-stone-200 bg-stone-800 hover:bg-stone-700 rounded-xl transition-colors whitespace-nowrap shrink-0 flex items-center gap-1.5 border border-stone-700 cursor-pointer"
          >
            <span>Website</span>
            <ExternalLink size={12} className="text-amber-400" />
          </a>
        </div>
      </header>

      {/* Main Viewport */}
      <main className="flex-1 max-w-7xl w-full mx-auto p-4 sm:p-6 flex flex-col pb-24">
        {/* Processing PDF Loading Banner */}
        {isProcessingPdf && (
          <div className="mb-4 p-4 bg-amber-500/20 border-2 border-amber-500/60 rounded-2xl flex items-center gap-3.5 text-xs text-amber-200 animate-pulse shadow-lg">
            <div className="w-6 h-6 border-2 border-amber-400 border-t-transparent rounded-full animate-spin shrink-0" />
            <div>
              <div className="font-bold text-amber-300 text-sm">Extracting PDF pages into high-resolution images...</div>
              <div className="text-[11px] text-stone-300">Each page is being extracted into the gallery. Once done, you can convert them to JPG, PNG, or WebP with 1 click!</div>
            </div>
          </div>
        )}

        {/* Dynamic Workflow Toast when photos are added */}
        {workflowToast && !isProcessingPdf && (
          <div className="mb-4 p-3 bg-amber-500/20 border border-amber-500/40 rounded-2xl flex items-center justify-between text-xs text-amber-200 animate-fade-in shadow-lg">
            <div className="flex items-center gap-2 font-semibold">
              <Sparkles size={16} className="text-amber-400 shrink-0" />
              <span>{workflowToast}</span>
            </div>
            <button
              onClick={() => setWorkflowToast(null)}
              className="text-amber-400 hover:text-white px-2 py-0.5 rounded font-bold"
            >
              ✕
            </button>
          </div>
        )}

        {/* Direct APK Download Banner */}
        {showApkBanner && (
          <div className="mb-4 p-3.5 sm:p-4 bg-gradient-to-r from-amber-950/40 via-stone-900 to-amber-950/20 border-2 border-amber-500/60 rounded-2xl flex flex-wrap items-center justify-between gap-3 shadow-lg">
            <div className="flex items-center gap-3">
              <div className="w-10 h-10 rounded-2xl bg-amber-500 flex items-center justify-center text-stone-950 font-black text-xl shadow-md shrink-0">
                🍩
              </div>
              <div>
                <div className="text-xs sm:text-sm font-bold text-white flex items-center gap-2">
                  <span>Download Donut Convert Android App</span>
                  <span className="text-[10px] bg-emerald-500/20 text-emerald-400 border border-emerald-500/30 px-1.5 py-0.5 rounded font-mono font-semibold">
                    API 36 build configured
                  </span>
                </div>
                <div className="text-[11px] sm:text-xs text-stone-300">
                  Installs as <strong className="text-amber-400 font-mono">donut convert</strong> after rebuilding the release APK
                </div>
              </div>
            </div>
            <div className="flex items-center gap-2">
              <button
                onClick={handleDownloadApk}
                className="px-4 py-2 bg-amber-500 hover:bg-amber-400 text-stone-950 font-bold text-xs rounded-xl shadow-md shadow-amber-500/20 flex items-center gap-1.5 transition-all active:scale-95 cursor-pointer"
              >
                <FolderDown size={14} />
                <span>Download Donut_Convert.apk</span>
              </button>
              <button
                onClick={() => setShowApkDownloadModal(true)}
                className="px-3.5 py-2 bg-stone-800 hover:bg-stone-700 text-stone-200 font-semibold text-xs rounded-xl border border-stone-700 flex items-center gap-1.5 transition-colors cursor-pointer"
              >
                <QrCode size={14} className="text-amber-400" />
                <span>Scan Phone QR</span>
              </button>
              <button
                onClick={() => setShowApkBanner(false)}
                className="p-2 text-stone-400 hover:text-white rounded-lg hover:bg-stone-800 transition-colors"
                title="Dismiss"
              >
                ✕
              </button>
            </div>
          </div>
        )}

        {/* 3-Step Visual Workflow Guide: "After adding photos then what next?" */}
        <div className="mb-5 grid grid-cols-1 md:grid-cols-3 gap-2.5 p-1.5 bg-stone-950/80 border border-stone-800 rounded-2xl shadow-sm">
          {/* Step 1 */}
          <div className={`p-3 rounded-xl border flex items-center gap-3 transition-all ${
            selectedItems.length > 0
              ? 'bg-emerald-950/20 border-emerald-500/30 text-emerald-300'
              : 'bg-stone-900 border-stone-800 text-stone-400'
          }`}>
            <div className={`w-7 h-7 rounded-full flex items-center justify-center font-bold text-xs shrink-0 ${
              selectedItems.length > 0 ? 'bg-emerald-500 text-stone-950' : 'bg-stone-800 text-stone-400'
            }`}>
              {selectedItems.length > 0 ? '✓' : '1'}
            </div>
            <div className="min-w-0">
              <div className="text-xs font-bold text-white">Step 1: Add &amp; Select Photos</div>
              <div className="text-[11px] text-stone-400 truncate">
                {selectedItems.length > 0 ? `${selectedItems.length} photos queued in sequence` : 'Upload or tap gallery photos'}
              </div>
            </div>
          </div>

          {/* Step 2 (Highlighted!) */}
          <div className="p-3 rounded-xl border bg-amber-500/10 border-amber-500 text-amber-200 ring-2 ring-amber-500/20 flex items-center gap-3 shadow-md">
            <div className="w-7 h-7 rounded-full flex items-center justify-center font-bold text-xs shrink-0 bg-amber-500 text-stone-950 animate-pulse">
              2
            </div>
            <div className="min-w-0">
              <div className="text-xs font-bold text-amber-400 flex items-center gap-1.5">
                <span>Step 2: Choose Output Format</span>
                <span className="text-[9px] bg-amber-500 text-stone-950 px-1.5 py-0.5 rounded font-black uppercase">
                  Current Step
                </span>
              </div>
              <div className="text-[11px] text-stone-300 truncate">
                Tap PDF, WebP, JPG, or PNG below
              </div>
            </div>
          </div>

          {/* Step 3 */}
          <div className={`p-3 rounded-xl border flex items-center gap-3 transition-all ${
            convertedFiles.length > 0
              ? 'bg-sky-950/20 border-sky-500/30 text-sky-300'
              : 'bg-stone-900 border-stone-800 text-stone-400'
          }`}>
            <div className={`w-7 h-7 rounded-full flex items-center justify-center font-bold text-xs shrink-0 ${
              convertedFiles.length > 0 ? 'bg-sky-500 text-stone-950' : 'bg-stone-800 text-stone-400'
            }`}>
              3
            </div>
            <div className="min-w-0">
              <div className="text-xs font-bold text-white">Step 3: Save &amp; Download</div>
              <div className="text-[11px] text-stone-400 truncate">
                {convertedFiles.length > 0 ? `${convertedFiles.length} file(s) converted & saved` : 'Auto-downloads to your device'}
              </div>
            </div>
          </div>
        </div>

        {/* Instant Action Toolbar */}
        <div className="mb-4 p-3.5 bg-stone-950/70 border border-stone-800 rounded-2xl flex flex-wrap items-center justify-between gap-3">
          <div className="flex items-center gap-3">
            <div className="w-2.5 h-2.5 rounded-full bg-emerald-400 animate-pulse" />
            <div className="text-xs">
              <span className="text-stone-400">Queue:</span>{' '}
              <strong className="text-white font-mono">{selectedItems.length} photos ready</strong>{' '}
              <span className="text-amber-400 font-semibold">· Tap any format to convert now:</span>
            </div>
          </div>

          {/* Quick Convert Buttons */}
          <div className="flex items-center gap-1.5 flex-wrap">
            <button
              onClick={() => handleInstantConvert('pdf')}
              className="px-3.5 py-1.5 rounded-xl bg-amber-500 hover:bg-amber-400 text-stone-950 text-xs font-bold transition-all shadow-sm flex items-center gap-1.5 active:scale-95 cursor-pointer"
            >
              <FileText size={14} />
              <span>Convert to PDF</span>
            </button>

            <button
              onClick={() => handleInstantConvert('webp')}
              className="px-3.5 py-1.5 rounded-xl bg-sky-500 hover:bg-sky-400 text-stone-950 text-xs font-bold transition-all shadow-sm flex items-center gap-1.5 active:scale-95 cursor-pointer"
            >
              <Zap size={14} />
              <span>Convert to WebP</span>
            </button>

            <button
              onClick={() => handleInstantConvert('jpg')}
              className="px-3.5 py-1.5 rounded-xl bg-emerald-500 hover:bg-emerald-400 text-stone-950 text-xs font-bold transition-all shadow-sm flex items-center gap-1.5 active:scale-95 cursor-pointer"
            >
              <ImageIcon size={14} />
              <span>Convert to JPG</span>
            </button>

            <button
              onClick={() => handleInstantConvert('png')}
              className="px-3.5 py-1.5 rounded-xl bg-violet-500 hover:bg-violet-400 text-stone-950 text-xs font-bold transition-all shadow-sm flex items-center gap-1.5 active:scale-95 cursor-pointer"
            >
              <Sparkles size={14} />
              <span>Convert to PNG</span>
            </button>
          </div>
        </div>

        {/* Main Workstation */}
        <div className="flex-1 flex flex-col">
            {activeTab === 'gallery' && (
              <div className="grid grid-cols-1 lg:grid-cols-12 gap-6 flex-1 items-start">
                {/* Left 7 Columns: Photo Gallery with Numbered Tap Sequence */}
                <div className="lg:col-span-7 flex flex-col h-full bg-stone-900/60 border border-stone-800/80 rounded-3xl p-5 shadow-lg">
                  <div className="flex-1">
                    <GalleryGrid
                      items={galleryItems}
                      selectedItemIds={selectedIds}
                      onToggleSelectItem={handleToggleSelectItem}
                      onClearSelection={handleClearSelection}
                      onSelectAll={handleSelectAll}
                      onUploadFiles={handleUploadFiles}
                      onDeleteItem={handleDeleteItem}
                      onResetSamples={() => setGalleryItems([])}
                      onOpenScanner={() => setShowScannerModal(true)}
                    />
                  </div>
                </div>

                {/* Right 5 Columns: The Radial Donut Picker Station */}
                <div className="lg:col-span-5 flex flex-col space-y-4">
                  {/* Radial Wheel Card */}
                  <div className="bg-stone-900/90 border border-stone-800/90 rounded-3xl p-6 shadow-xl flex flex-col items-center text-center relative overflow-hidden">
                    <div className="w-full flex items-center justify-between pb-3 mb-2 border-b border-stone-800/80">
                      <div className="text-left">
                        <div className="text-xs font-bold font-display uppercase tracking-wider text-amber-500">
                          Radial Donut Wheel
                        </div>
                        <h2 className="text-sm font-bold text-stone-200">
                          Click Any Sector or Center
                        </h2>
                      </div>

                      <div className="px-2.5 py-1 rounded-full bg-stone-800 text-[11px] font-mono text-amber-400 font-bold">
                        {selectedItems.length} queued
                      </div>
                    </div>

                    {/* Donut Wheel */}
                    <div className="my-2">
                      <RadialPicker
                        selectedCount={selectedItems.length}
                        onSelectFormat={handleSelectFormat}
                        activeFormat={activeFormat}
                      />
                    </div>

                    {/* Selection Summary Strip */}
                    <div className="w-full mt-4 p-3.5 bg-stone-950/60 border border-stone-800 rounded-2xl text-left">
                      <div className="text-xs font-bold text-stone-200 mb-1 flex items-center justify-between">
                        <span>Queued Sequence Order</span>
                        <span className="text-[11px] text-stone-400">
                          {selectedItems.length} selected
                        </span>
                      </div>

                      {selectedItems.length === 0 ? (
                        <p className="text-[11px] text-stone-500">
                          No items chosen yet. Click any photo in the gallery to begin.
                        </p>
                      ) : (
                        <div className="flex items-center gap-1.5 overflow-x-auto py-1">
                          {selectedItems.map((item, idx) => (
                            <div
                              key={item.id}
                              className="relative shrink-0 w-10 h-10 rounded-lg overflow-hidden border border-stone-700"
                              title={`${idx + 1}. ${item.name}`}
                            >
                              <img
                                src={item.url}
                                alt={item.name}
                                className="w-full h-full object-cover"
                              />
                              <div className="absolute top-0 left-0 w-4 h-4 bg-amber-500 text-stone-950 font-bold text-[9px] flex items-center justify-center rounded-br">
                                {idx + 1}
                              </div>
                            </div>
                          ))}
                        </div>
                      )}
                    </div>
                  </div>

                  {/* Mac & Windows Website Info Card */}
                  <div className="bg-stone-900/60 border border-stone-800 rounded-3xl p-5 space-y-3">
                    <div className="flex items-center justify-between">
                      <h3 className="text-xs font-bold uppercase tracking-wider text-amber-400 flex items-center gap-2">
                        <Sparkles size={14} />
                        <span>Mac &amp; Windows Desktop Website</span>
                      </h3>
                      <a
                        href={WEBSITE_URL}
                        target="_blank"
                        rel="noopener noreferrer"
                        className="text-xs font-bold text-amber-400 hover:text-amber-300 flex items-center gap-1"
                      >
                        <span>Visit Site</span>
                        <ExternalLink size={12} />
                      </a>
                    </div>
                    <p className="text-xs text-stone-300">
                      Already have Donut Convert for Mac and Windows? Anyone can access your desktop version at{' '}
                      <a
                        href={WEBSITE_URL}
                        target="_blank"
                        rel="noopener noreferrer"
                        className="text-amber-400 underline font-mono text-[11px]"
                      >
                        user-cariv.github.io/Donut-Convert
                      </a>.
                    </p>
                  </div>
                </div>
              </div>
            )}

            {activeTab === 'history' && (
              <div className="bg-stone-900/60 border border-stone-800 rounded-3xl p-6 shadow-xl flex-1">
                <div className="pb-4 mb-4 border-b border-stone-800 flex items-center justify-between">
                  <div>
                    <h1 className="text-lg font-bold font-display text-white">
                      Saved Conversions Library
                    </h1>
                    <p className="text-xs text-stone-400">
                      Files are processed client-side and saved safely (Images → Pictures/Donut, PDFs → Downloads/Donut)
                    </p>
                  </div>
                  <button
                    onClick={() => setActiveTab('gallery')}
                    className="px-3.5 py-1.5 rounded-xl bg-amber-500 text-stone-950 font-bold text-xs hover:bg-amber-400 transition-colors"
                  >
                    Back to Converter
                  </button>
                </div>

                <ConvertedHistory
                  files={convertedFiles}
                  onDeleteFile={(id) => setConvertedFiles((prev) => prev.filter((f) => f.id !== id))}
                  onClearAll={() => setConvertedFiles([])}
                />
              </div>
            )}
          </div>
      </main>

      {/* Donut™ Trademark & Footer */}
      <footer className="mt-auto border-t border-stone-800 bg-stone-950/80 backdrop-blur-md px-4 sm:px-8 pt-8 pb-24 sm:pb-16 text-stone-400">
        <div className="max-w-7xl mx-auto flex flex-col gap-6">
          <div className="flex flex-col md:flex-row items-center justify-between gap-4 pb-6 border-b border-stone-800/60">
            {/* Brand and Trademark Notice */}
            <div className="flex items-center gap-3">
              <div className="w-8 h-8 rounded-full border-2 border-amber-500/80 bg-stone-900 flex items-center justify-center shadow-md shadow-amber-500/10">
                <div className="w-2.5 h-2.5 rounded-full bg-amber-400 ring-2 ring-stone-900" />
              </div>
              <div>
                <div className="flex items-center gap-1.5">
                  <span className="font-extrabold text-stone-100 tracking-tight text-sm">Donut™</span>
                  <span className="text-amber-500 font-semibold text-xs">Convert</span>
                  <span className="text-[10px] uppercase tracking-wider px-1.5 py-0.5 rounded bg-amber-500/10 text-amber-400 border border-amber-500/30 font-mono">
                    Trademark
                  </span>
                </div>
                <p className="text-[11px] text-stone-500 mt-0.5">
                  Universal High-Resolution Image &amp; Document Conversion Suite
                </p>
              </div>
            </div>

            {/* Quick links & Website */}
            <div className="flex flex-wrap items-center justify-center md:justify-end gap-3 text-xs">
              <a
                href={WEBSITE_URL}
                target="_blank"
                rel="noopener noreferrer"
                className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg bg-stone-900 hover:bg-stone-800 border border-stone-700/80 text-amber-400 hover:text-amber-300 transition-colors shadow-sm font-medium"
              >
                <span>Official Donut™ Website</span>
                <ExternalLink size={12} />
              </a>
              <span className="text-stone-700 hidden sm:inline">•</span>
              <span className="text-[11px] text-stone-400">100% Client-Side Processing</span>
            </div>
          </div>

          <div className="flex flex-col sm:flex-row items-center justify-between gap-3 text-[11px] text-stone-500">
            <p className="text-center sm:text-left">
              © {new Date().getFullYear()} <strong className="text-stone-300 font-semibold">Donut™</strong>. Donut Convert™ and the Donut ring insignia are registered trademarks. All rights reserved.
            </p>
            <div className="flex items-center gap-3 text-stone-400">
              <span className="hover:text-stone-300 transition-colors">Privacy First</span>
              <span>·</span>
              <span className="hover:text-stone-300 transition-colors">Zero Cloud Uploads</span>
              <span>·</span>
              <span className="hover:text-stone-300 transition-colors">Android &amp; Web</span>
            </div>
          </div>
        </div>
      </footer>


      {/* Sticky Bottom "Next Step" Action Bar: Always guides user what to do next */}
      {selectedItems.length > 0 && activeTab === 'gallery' && (
        <div className="fixed bottom-4 left-1/2 -translate-x-1/2 z-40 max-w-3xl w-[94%] bg-stone-900/95 border-2 border-amber-500/80 shadow-[0_10px_40px_rgba(0,0,0,0.8)] rounded-2xl p-3 backdrop-blur-md flex flex-wrap items-center justify-between gap-3 animate-fade-in ring-1 ring-amber-500/30">
          <div className="flex items-center gap-3">
            <div className="w-9 h-9 rounded-xl bg-amber-500 text-stone-950 font-bold text-sm flex items-center justify-center shrink-0 shadow-sm">
              {selectedItems.length}
            </div>
            <div>
              <div className="text-xs font-bold text-white flex items-center gap-1.5">
                <span>{selectedItems.length} {selectedItems.length === 1 ? 'photo' : 'photos'} ready!</span>
                <span className="text-[11px] text-amber-400 font-semibold">→ Next: Tap a format to convert</span>
              </div>
              <div className="text-[10px] text-stone-400">
                Tap order = page order (1 to {selectedItems.length})
              </div>
            </div>
          </div>

          <div className="flex items-center gap-2 flex-wrap">
            <button
              onClick={() => handleInstantConvert('pdf')}
              className="px-4 py-2 rounded-xl bg-amber-500 hover:bg-amber-400 text-stone-950 font-bold text-xs shadow-md shadow-amber-500/20 active:scale-95 flex items-center gap-1.5 cursor-pointer"
            >
              <FileText size={15} strokeWidth={2.5} />
              <span>Convert to PDF</span>
            </button>

            <button
              onClick={() => handleInstantConvert('webp')}
              className="px-3.5 py-2 rounded-xl bg-sky-500 hover:bg-sky-400 text-stone-950 font-bold text-xs shadow-md shadow-sky-500/20 active:scale-95 flex items-center gap-1.5 cursor-pointer"
            >
              <Zap size={15} strokeWidth={2.5} />
              <span>WebP</span>
            </button>

            <button
              onClick={() => handleInstantConvert('jpg')}
              className="px-3.5 py-2 rounded-xl bg-emerald-500 hover:bg-emerald-400 text-stone-950 font-bold text-xs shadow-md shadow-emerald-500/20 active:scale-95 flex items-center gap-1.5 cursor-pointer"
            >
              <ImageIcon size={15} strokeWidth={2.5} />
              <span>JPG</span>
            </button>

            <button
              onClick={() => handleInstantConvert('png')}
              className="px-3.5 py-2 rounded-xl bg-violet-500 hover:bg-violet-400 text-stone-950 font-bold text-xs shadow-md shadow-violet-500/20 active:scale-95 flex items-center gap-1.5 cursor-pointer"
            >
              <Sparkles size={15} strokeWidth={2.5} />
              <span>PNG</span>
            </button>
          </div>
        </div>
      )}

      {/* Floating Donut Bubble */}
      <FloatingBubble
        selectedCount={selectedItems.length}
        onSelectFormat={handleSelectFormat}
        onUploadFiles={handleUploadFiles}
        isOpen={showFloatingBubble}
        onToggleBubble={() => setShowFloatingBubble(!showFloatingBubble)}
      />

      {/* PDF Options Sheet */}
      <PdfOptionsSheet
        isOpen={showPdfSheet}
        onClose={() => setShowPdfSheet(false)}
        selectedItems={selectedItems}
        onReorderItems={handleReorderItems}
        onStartConversion={handleStartPdfConversion}
      />

      {/* Image Options Sheet (JPG / PNG / WEBP) */}
      <ImageOptionsSheet
        isOpen={showImageSheet}
        onClose={() => setShowImageSheet(false)}
        format={activeFormat as 'jpg' | 'png' | 'webp'}
        selectedItems={selectedItems}
        onStartConversion={handleStartImageConversion}
      />

      {/* Live Conversion Progress & Download Modal */}
      <ConversionModal
        isOpen={showConversionModal}
        onClose={() => setShowConversionModal(false)}
        progress={conversionProgress}
        results={currentJobResults}
        onOpenHistory={() => {
          setActiveTab('history');
        }}
      />

      {/* Camera Document Scanner Modal */}
      <CameraScannerModal
        isOpen={showScannerModal}
        onClose={() => setShowScannerModal(false)}
        onAddScannedItems={handleAddScannedItems}
      />

      {/* Dedicated APK Download & QR Scanner Modal */}
      <ApkDownloadModal
        isOpen={showApkDownloadModal}
        onClose={() => setShowApkDownloadModal(false)}
      />

      {/* Android APK & Play Store Release Modal */}
      {showApkGuideModal && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-stone-950/85 backdrop-blur-md animate-fade-in">
          <div className="w-full max-w-2xl bg-stone-900 border border-stone-800 rounded-3xl p-6 shadow-2xl relative overflow-hidden flex flex-col max-h-[90vh]">
            <div className="flex items-center justify-between pb-4 border-b border-stone-800">
              <div className="flex items-center gap-3">
                <div className="w-10 h-10 rounded-xl bg-amber-500/10 border border-amber-500/30 flex items-center justify-center text-amber-500">
                  <Smartphone size={22} />
                </div>
                <div>
                  <h2 className="text-lg font-bold font-display text-white">
                    Android APK &amp; Play Store Package
                  </h2>
                  <p className="text-xs text-stone-400">
                    Host the APK on your website and release to Google Play
                  </p>
                </div>
              </div>

              <button
                onClick={() => setShowApkGuideModal(false)}
                className="p-2 text-stone-400 hover:text-white rounded-lg hover:bg-stone-800"
              >
                ✕
              </button>
            </div>

            <div className="py-5 overflow-y-auto space-y-4 flex-1">
              {/* Direct 1-Click APK Download */}
              <div className="p-4 bg-amber-500/15 border-2 border-amber-500/80 rounded-2xl flex flex-wrap items-center justify-between gap-3 shadow-lg">
                <div className="space-y-0.5">
                  <div className="text-sm font-bold text-amber-300 flex items-center gap-2">
                    <Smartphone size={16} />
                    <span>Direct APK Download (Host & Share)</span>
                  </div>
                  <div className="text-xs text-stone-300">
                    File: <strong className="text-white font-mono">Donut_Convert.apk</strong> (replace with the signed API 36 build) • Phone App Label: <strong className="text-amber-400 font-mono">donut convert</strong>
                  </div>
                </div>

                <a
                  href="/Donut_Convert.apk"
                  download="Donut_Convert.apk"
                  className="px-4 py-2.5 bg-amber-500 hover:bg-amber-400 text-stone-950 font-bold text-xs rounded-xl flex items-center gap-2 shadow-md shadow-amber-500/30 transition-all active:scale-95 cursor-pointer"
                >
                  <FolderDown size={14} />
                  <span>Download Donut_Convert.apk</span>
                </a>
              </div>

              {/* Website Hosting Notice */}
              <div className="p-4 bg-stone-950/80 border border-amber-500/30 rounded-2xl flex flex-wrap items-center justify-between gap-3 shadow-md">
                <div>
                  <div className="text-sm font-bold text-amber-400 flex items-center gap-2">
                    <Sparkles size={16} />
                    <span>Website Direct Download</span>
                  </div>
                  <div className="text-xs text-stone-300 mt-0.5">
                    Your APK is distributed directly via your website at <a href={WEBSITE_URL} target="_blank" rel="noopener noreferrer" className="text-amber-400 underline font-mono">user-cariv.github.io/Donut-Convert</a>.
                  </div>
                </div>

                <a
                  href={WEBSITE_URL}
                  target="_blank"
                  rel="noopener noreferrer"
                  className="px-4 py-2 bg-stone-800 hover:bg-stone-700 text-amber-400 hover:text-amber-300 font-bold text-xs rounded-xl border border-stone-700 flex items-center gap-1.5 transition-all cursor-pointer"
                >
                  <span>Open Website</span>
                  <ExternalLink size={12} />
                </a>
              </div>

              {/* Option 1: Automatic GitHub Actions (Direct to your website) */}
              <div className="p-4 bg-stone-950/70 border border-stone-800 rounded-2xl space-y-2">
                <div className="flex items-center gap-2 text-amber-400 text-xs font-bold uppercase tracking-wider">
                  <Sparkles size={14} />
                  <span>1. Automated Cloud Build (For your website)</span>
                </div>
                <p className="text-xs text-stone-300">
                  Build a signed release with the Android project and upload the verified APK to your hosting provider. For the safest installation and automatic updates, publish the signed AAB through Google Play; direct APK downloads may trigger Android or Samsung unknown-app protections.
                </p>
              </div>

              {/* Option 2: Build APK for Website */}
              <div className="p-4 bg-stone-950/70 border border-stone-800 rounded-2xl space-y-2">
                <div className="flex items-center justify-between">
                  <div className="text-xs font-bold text-emerald-400 uppercase tracking-wider flex items-center gap-2">
                    <Code2 size={14} />
                    <span>2. Build APK for Website</span>
                  </div>
                  <button
                    onClick={() => handleCopyApkCommand('./gradlew assembleRelease')}
                    className="text-[11px] px-2.5 py-1 bg-stone-800 hover:bg-stone-700 text-stone-200 rounded font-mono flex items-center gap-1 cursor-pointer"
                  >
                    {copyApkCmdSuccess ? <Check size={12} className="text-emerald-400" /> : <Copy size={12} />}
                    <span>Copy Command</span>
                  </button>
                </div>
                <div className="p-2.5 bg-stone-900 border border-stone-800 rounded-xl font-mono text-xs text-amber-300">
                  ./gradlew assembleRelease
                </div>
                <p className="text-[11px] text-stone-400">
                  Generates <code className="text-stone-200">app/build/outputs/apk/release/app-release.apk</code> to upload directly to your website.
                </p>
              </div>

              {/* Option 3: Build AAB for Google Play Store */}
              <div className="p-4 bg-stone-950/70 border border-stone-800 rounded-2xl space-y-2">
                <div className="flex items-center justify-between">
                  <div className="text-xs font-bold text-sky-400 uppercase tracking-wider flex items-center gap-2">
                    <FileCheck2 size={14} />
                    <span>3. Build AAB for Google Play Store</span>
                  </div>
                  <button
                    onClick={() => handleCopyApkCommand('./gradlew bundleRelease')}
                    className="text-[11px] px-2.5 py-1 bg-stone-800 hover:bg-stone-700 text-stone-200 rounded font-mono flex items-center gap-1 cursor-pointer"
                  >
                    <Copy size={12} />
                    <span>Copy Command</span>
                  </button>
                </div>
                <div className="p-2.5 bg-stone-900 border border-stone-800 rounded-xl font-mono text-xs text-sky-300">
                  ./gradlew bundleRelease
                </div>
                <p className="text-[11px] text-stone-400">
                  Google Play Store requires an <strong className="text-stone-200">.aab</strong> (Android App Bundle). This command creates <code className="text-stone-200">app/build/outputs/bundle/release/app-release.aab</code> for Google Play Console.
                </p>
              </div>
            </div>

            <div className="pt-4 border-t border-stone-800 flex items-center justify-end">
              <button
                onClick={() => setShowApkGuideModal(false)}
                className="px-5 py-2 rounded-xl bg-amber-500 hover:bg-amber-400 text-stone-950 font-bold text-xs"
              >
                Got It
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
