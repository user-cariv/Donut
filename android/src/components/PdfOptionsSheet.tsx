import React, { useState, useEffect } from 'react';
import { motion, AnimatePresence } from 'motion/react';
import {
  X,
  FileText,
  Files,
  Sliders,
  ArrowUp,
  ArrowDown,
  Layers,
  Settings2,
  Check,
  RotateCw,
  Maximize2
} from 'lucide-react';
import { GalleryItem, MarginOption, PageOrientation, PageSize, PdfOptions } from '../types';
import { formatBytes } from '../utils/converter';

interface PdfOptionsSheetProps {
  isOpen: boolean;
  onClose: () => void;
  selectedItems: GalleryItem[];
  onReorderItems: (items: GalleryItem[]) => void;
  onStartConversion: (options: PdfOptions) => void;
}

const STORAGE_KEY = 'donut_convert_pdf_prefs_v1';

export const PdfOptionsSheet: React.FC<PdfOptionsSheetProps> = ({
  isOpen,
  onClose,
  selectedItems,
  onReorderItems,
  onStartConversion
}) => {
  const [mode, setMode] = useState<'single' | 'separate'>('single');
  const [pageSize, setPageSize] = useState<PageSize>('a4');
  const [orientation, setOrientation] = useState<PageOrientation>('auto');
  const [margins, setMargins] = useState<MarginOption>('none');
  const [quality, setQuality] = useState<number>(0.85);
  const [targetSizeKb, setTargetSizeKb] = useState<number | undefined>(undefined);
  const [watermarkText, setWatermarkText] = useState<string>('');
  const [filenamePattern, setFilenamePattern] = useState<string>('Donut_Doc_{date}');
  const [rememberChoice, setRememberChoice] = useState<boolean>(true);
  const [activeTab, setActiveTab] = useState<'options' | 'reorder'>('options');

  // Load saved preferences on mount (Prefs.kt equivalent)
  useEffect(() => {
    try {
      const saved = localStorage.getItem(STORAGE_KEY);
      if (saved) {
        const parsed = JSON.parse(saved);
        if (parsed.mode) setMode(parsed.mode);
        if (parsed.pageSize) setPageSize(parsed.pageSize);
        if (parsed.orientation) setOrientation(parsed.orientation);
        if (parsed.margins) setMargins(parsed.margins);
        if (typeof parsed.quality === 'number') setQuality(parsed.quality);
        if (parsed.filenamePattern) setFilenamePattern(parsed.filenamePattern);
        setRememberChoice(true);
      }
    } catch {
      // ignore
    }
  }, []);

  if (!isOpen) return null;

  const handleSaveAndConvert = () => {
    const opts: PdfOptions = {
      mode,
      pageSize,
      orientation,
      margins,
      quality,
      filenamePattern: filenamePattern.trim() || 'Donut_Doc_{date}',
      rememberChoice,
      targetSizeKb,
      watermarkText: watermarkText.trim() || undefined
    };

    if (rememberChoice) {
      try {
        localStorage.setItem(STORAGE_KEY, JSON.stringify(opts));
      } catch {
        // ignore
      }
    }

    onStartConversion(opts);
  };

  const moveItem = (index: number, direction: 'up' | 'down') => {
    const newItems = [...selectedItems];
    const targetIndex = direction === 'up' ? index - 1 : index + 1;
    if (targetIndex < 0 || targetIndex >= newItems.length) return;

    const temp = newItems[index];
    newItems[index] = newItems[targetIndex];
    newItems[targetIndex] = temp;

    // re-assign 1-indexed selectedOrder
    newItems.forEach((it, idx) => {
      it.selectedOrder = idx + 1;
    });

    onReorderItems(newItems);
  };

  // Estimate output size
  const totalRawBytes = selectedItems.reduce((acc, it) => acc + it.sizeBytes, 0);
  const estimatedBytes = Math.round(totalRawBytes * (quality * 0.75 + 0.1));

  return (
    <div className="fixed inset-0 z-50 flex items-end sm:items-center justify-center p-0 sm:p-4 bg-stone-950/80 backdrop-blur-sm animate-fade-in">
      <motion.div
        initial={{ y: 80, opacity: 0 }}
        animate={{ y: 0, opacity: 1 }}
        exit={{ y: 80, opacity: 0 }}
        transition={{ duration: 0.22, ease: [0.16, 1, 0.3, 1] }}
        className="w-full max-w-xl bg-stone-900 border border-stone-800 rounded-t-3xl sm:rounded-3xl shadow-2xl flex flex-col max-h-[90vh] overflow-hidden"
      >
        {/* Grab Handle for mobile */}
        <div className="w-12 h-1.5 bg-stone-700 rounded-full mx-auto mt-3 sm:hidden" />

        {/* Header */}
        <div className="flex items-center justify-between px-6 py-4 border-b border-stone-800">
          <div className="flex items-center gap-3">
            <div className="w-9 h-9 rounded-xl bg-amber-500/10 border border-amber-500/30 flex items-center justify-center text-amber-500">
              <FileText size={20} />
            </div>
            <div>
              <h2 className="text-lg font-bold font-display text-white">PDF Export Setup</h2>
              <div className="text-xs text-stone-400">
                {selectedItems.length} {selectedItems.length === 1 ? 'page' : 'pages'} selected
              </div>
            </div>
          </div>

          <button
            onClick={onClose}
            className="p-2 text-stone-400 hover:text-white rounded-lg hover:bg-stone-800 transition-colors"
          >
            <X size={20} />
          </button>
        </div>

        {/* Tab switch: Options vs Page Order */}
        <div className="flex px-6 pt-3 pb-1 border-b border-stone-800/60 bg-stone-900/50">
          <button
            onClick={() => setActiveTab('options')}
            className={`pb-2.5 px-3 text-xs font-semibold border-b-2 transition-all flex items-center gap-1.5 ${
              activeTab === 'options'
                ? 'border-amber-500 text-amber-400'
                : 'border-transparent text-stone-400 hover:text-stone-200'
            }`}
          >
            <Settings2 size={14} />
            <span>Document Settings</span>
          </button>

          <button
            onClick={() => setActiveTab('reorder')}
            className={`pb-2.5 px-3 text-xs font-semibold border-b-2 transition-all flex items-center gap-1.5 ${
              activeTab === 'reorder'
                ? 'border-amber-500 text-amber-400'
                : 'border-transparent text-stone-400 hover:text-stone-200'
            }`}
          >
            <Layers size={14} />
            <span>Page Order ({selectedItems.length})</span>
          </button>
        </div>

        {/* Body Content */}
        <div className="p-6 overflow-y-auto space-y-6 flex-1">
          {activeTab === 'options' ? (
            <>
              {/* Mode: One PDF vs Separate PDFs */}
              <div>
                <label className="text-xs font-bold text-stone-300 uppercase tracking-wider block mb-2">
                  Output Structure
                </label>
                <div className="grid grid-cols-2 gap-3">
                  <button
                    type="button"
                    onClick={() => setMode('single')}
                    className={`p-3.5 rounded-xl border text-left transition-all ${
                      mode === 'single'
                        ? 'bg-amber-500/10 border-amber-500 text-white shadow-sm'
                        : 'bg-stone-800/40 border-stone-800 text-stone-400 hover:bg-stone-800/80 hover:text-stone-200'
                    }`}
                  >
                    <div className="flex items-center gap-2 mb-1">
                      <FileText size={16} className={mode === 'single' ? 'text-amber-400' : 'text-stone-400'} />
                      <span className="font-bold text-sm text-stone-100">One Combined PDF</span>
                    </div>
                    <p className="text-[11px] text-stone-400 leading-tight">
                      Merges all {selectedItems.length} photos into a single multi-page PDF document
                    </p>
                  </button>

                  <button
                    type="button"
                    onClick={() => setMode('separate')}
                    className={`p-3.5 rounded-xl border text-left transition-all ${
                      mode === 'separate'
                        ? 'bg-amber-500/10 border-amber-500 text-white shadow-sm'
                        : 'bg-stone-800/40 border-stone-800 text-stone-400 hover:bg-stone-800/80 hover:text-stone-200'
                    }`}
                  >
                    <div className="flex items-center gap-2 mb-1">
                      <Files size={16} className={mode === 'separate' ? 'text-amber-400' : 'text-stone-400'} />
                      <span className="font-bold text-sm text-stone-100">Separate PDFs</span>
                    </div>
                    <p className="text-[11px] text-stone-400 leading-tight">
                      Creates {selectedItems.length} individual PDF files (download individually or ZIP)
                    </p>
                  </button>
                </div>
              </div>

              {/* Page Size & Orientation */}
              <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                <div>
                  <label className="text-xs font-bold text-stone-300 uppercase tracking-wider block mb-2">
                    Page Format
                  </label>
                  <select
                    value={pageSize}
                    onChange={(e) => setPageSize(e.target.value as PageSize)}
                    className="w-full px-3.5 py-2.5 rounded-xl bg-stone-800 border border-stone-700 text-stone-100 text-sm focus:outline-none focus:border-amber-500"
                  >
                    <option value="a4">A4 (210 × 297 mm)</option>
                    <option value="letter">US Letter (8.5 × 11 in)</option>
                    <option value="fit">Auto-Fit to Image</option>
                    <option value="original">Original Dimensions</option>
                  </select>
                </div>

                <div>
                  <label className="text-xs font-bold text-stone-300 uppercase tracking-wider block mb-2">
                    Orientation
                  </label>
                  <select
                    value={orientation}
                    onChange={(e) => setOrientation(e.target.value as PageOrientation)}
                    className="w-full px-3.5 py-2.5 rounded-xl bg-stone-800 border border-stone-700 text-stone-100 text-sm focus:outline-none focus:border-amber-500"
                  >
                    <option value="auto">Auto (Match Image)</option>
                    <option value="portrait">Always Portrait</option>
                    <option value="landscape">Always Landscape</option>
                  </select>
                </div>
              </div>

              {/* Margins */}
              <div>
                <label className="text-xs font-bold text-stone-300 uppercase tracking-wider block mb-2">
                  Page Margins
                </label>
                <div className="grid grid-cols-3 gap-2">
                  {[
                    { id: 'none', label: 'None (Bleed)' },
                    { id: 'narrow', label: 'Narrow (10pt)' },
                    { id: 'standard', label: 'Standard (20pt)' }
                  ].map((m) => (
                    <button
                      key={m.id}
                      type="button"
                      onClick={() => setMargins(m.id as MarginOption)}
                      className={`py-2 px-3 rounded-lg text-xs font-semibold border transition-all ${
                        margins === m.id
                          ? 'bg-amber-500 text-stone-950 border-amber-500'
                          : 'bg-stone-800/60 text-stone-400 border-stone-700/60 hover:text-stone-200'
                      }`}
                    >
                      {m.label}
                    </button>
                  ))}
                </div>
              </div>

              {/* Compression Quality Slider */}
              <div>
                <div className="flex items-center justify-between mb-2">
                  <label className="text-xs font-bold text-stone-300 uppercase tracking-wider">
                    Quality &amp; Compression
                  </label>
                  <span className="text-xs font-bold text-amber-400 tabular-nums">
                    {Math.round(quality * 100)}% ({quality >= 0.9 ? 'Crisp' : quality >= 0.75 ? 'Balanced' : 'Eco Compact'})
                  </span>
                </div>
                <input
                  type="range"
                  min="0.3"
                  max="1.0"
                  step="0.05"
                  value={quality}
                  onChange={(e) => setQuality(parseFloat(e.target.value))}
                  className="w-full accent-amber-500 cursor-pointer"
                />
                <div className="flex items-center justify-between text-[11px] text-stone-500 mt-1">
                  <span>Small file size</span>
                  <span className="text-stone-400">Est. size: ~{formatBytes(estimatedBytes)}</span>
                  <span>Maximum fidelity</span>
                </div>
              </div>

              {/* File Name Pattern */}
              <div>
                <label className="text-xs font-bold text-stone-300 uppercase tracking-wider block mb-2">
                  File Name Template
                </label>
                <div className="flex items-center gap-2">
                  <input
                    type="text"
                    value={filenamePattern}
                    onChange={(e) => setFilenamePattern(e.target.value)}
                    placeholder="Donut_Doc_{date}"
                    className="flex-1 px-3.5 py-2 rounded-xl bg-stone-800 border border-stone-700 text-stone-100 text-sm font-mono focus:outline-none focus:border-amber-500"
                  />
                  <span className="text-xs text-stone-500 font-mono">.pdf</span>
                </div>
                <p className="text-[11px] text-stone-500 mt-1">
                  Tokens available: <code className="text-amber-400">&#123;date&#125;</code>, <code className="text-amber-400">&#123;time&#125;</code>, <code className="text-amber-400">&#123;name&#125;</code>, <code className="text-amber-400">&#123;index&#125;</code>
                </p>
              </div>

              {/* Target File Size Limit (Android feature for portal limits) */}
              <div>
                <div className="flex items-center justify-between mb-2">
                  <label className="text-xs font-bold text-stone-300 uppercase tracking-wider">
                    Target File Size Limit (Optional)
                  </label>
                  {targetSizeKb && (
                    <span className="text-xs font-bold text-emerald-400">
                      Under {targetSizeKb} KB
                    </span>
                  )}
                </div>
                <div className="grid grid-cols-4 gap-2">
                  {[
                    { id: undefined, label: 'No Limit' },
                    { id: 100, label: '100 KB' },
                    { id: 200, label: '200 KB' },
                    { id: 500, label: '500 KB' }
                  ].map((preset) => (
                    <button
                      key={preset.label}
                      type="button"
                      onClick={() => setTargetSizeKb(preset.id)}
                      className={`py-2 px-2 text-center rounded-xl text-xs font-semibold border transition-all cursor-pointer ${
                        targetSizeKb === preset.id
                          ? 'bg-amber-500 text-stone-950 border-amber-500 shadow-sm font-bold'
                          : 'bg-stone-800/60 text-stone-400 border-stone-700/60 hover:text-stone-200'
                      }`}
                    >
                      {preset.label}
                    </button>
                  ))}
                </div>
                <p className="text-[11px] text-stone-500 mt-1">
                  Compresses document to stay under job portal or government size limits.
                </p>
              </div>

              {/* Watermark (Optional) */}
              <div>
                <label className="text-xs font-bold text-stone-300 uppercase tracking-wider block mb-2">
                  Document Watermark (Optional)
                </label>
                <input
                  type="text"
                  value={watermarkText}
                  onChange={(e) => setWatermarkText(e.target.value)}
                  placeholder="e.g. CONFIDENTIAL, DRAFT, or Your Name"
                  className="w-full px-3.5 py-2.5 rounded-xl bg-stone-800 border border-stone-700 text-stone-100 text-xs focus:outline-none focus:border-amber-500"
                />
                <p className="text-[11px] text-stone-500 mt-1">
                  Adds a semi-transparent diagonal watermark across each PDF page.
                </p>
              </div>

              {/* Remember my choice (Prefs.kt) */}
              <div className="pt-2 border-t border-stone-800/80">
                <label className="flex items-center gap-2.5 cursor-pointer text-xs text-stone-300">
                  <input
                    type="checkbox"
                    checked={rememberChoice}
                    onChange={(e) => setRememberChoice(e.target.checked)}
                    className="rounded border-stone-700 text-amber-500 focus:ring-amber-500 bg-stone-800"
                  />
                  <span>Remember my preferences for future PDF exports</span>
                </label>
              </div>
            </>
          ) : (
            /* Reorder Pages View */
            <div className="space-y-2">
              <p className="text-xs text-stone-400 mb-3">
                Tap order is your page order. Use arrows to re-sequence pages in the PDF document.
              </p>
              {selectedItems.map((item, index) => (
                <div
                  key={item.id}
                  className="flex items-center gap-3 p-2.5 bg-stone-800/60 border border-stone-700/60 rounded-xl"
                >
                  <div className="w-7 h-7 rounded-full bg-amber-500 text-stone-950 font-bold text-xs flex items-center justify-center shrink-0">
                    {index + 1}
                  </div>

                  <img
                    src={item.url}
                    alt={item.name}
                    className="w-12 h-12 object-cover rounded-lg border border-stone-700 shrink-0"
                  />

                  <div className="flex-1 min-w-0">
                    <div className="text-xs font-semibold text-stone-200 truncate">{item.name}</div>
                    <div className="text-[11px] text-stone-400">
                      {item.width} × {item.height} · {formatBytes(item.sizeBytes)}
                    </div>
                  </div>

                  <div className="flex items-center gap-1 shrink-0">
                    <button
                      type="button"
                      disabled={index === 0}
                      onClick={() => moveItem(index, 'up')}
                      className="p-1.5 rounded-lg text-stone-400 hover:text-white hover:bg-stone-700 disabled:opacity-30 disabled:hover:bg-transparent"
                    >
                      <ArrowUp size={16} />
                    </button>
                    <button
                      type="button"
                      disabled={index === selectedItems.length - 1}
                      onClick={() => moveItem(index, 'down')}
                      className="p-1.5 rounded-lg text-stone-400 hover:text-white hover:bg-stone-700 disabled:opacity-30 disabled:hover:bg-transparent"
                    >
                      <ArrowDown size={16} />
                    </button>
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>

        {/* Footer Actions */}
        <div className="p-4 sm:p-6 border-t border-stone-800 bg-stone-900/90 flex items-center justify-between gap-4">
          <div className="text-xs text-stone-400 hidden sm:block">
            {mode === 'single' ? '1 PDF file' : `${selectedItems.length} individual PDFs`} · ~{formatBytes(estimatedBytes)}
          </div>

          <div className="flex items-center gap-2 w-full sm:w-auto">
            <button
              type="button"
              onClick={onClose}
              className="flex-1 sm:flex-initial px-4 py-2.5 rounded-xl border border-stone-700 text-stone-300 text-xs font-bold hover:bg-stone-800 transition-colors"
            >
              Cancel
            </button>

            <button
              type="button"
              onClick={handleSaveAndConvert}
              className="flex-1 sm:flex-initial px-6 py-2.5 rounded-xl bg-amber-500 hover:bg-amber-400 text-stone-950 text-xs font-bold transition-all shadow-lg shadow-amber-500/20 active:scale-[0.98] flex items-center justify-center gap-2"
            >
              <FileText size={16} strokeWidth={2.5} />
              <span>Convert to PDF</span>
            </button>
          </div>
        </div>
      </motion.div>
    </div>
  );
};
