import React, { useRef, useState } from 'react';
import { motion, AnimatePresence } from 'motion/react';
import {
  Upload,
  Check,
  Eye,
  Trash2,
  Plus,
  RefreshCw,
  FolderOpen,
  Filter,
  FileText,
  Camera,
  Layers
} from 'lucide-react';
import { GalleryItem } from '../types';
import { formatBytes } from '../utils/converter';
import { playHapticTick } from '../utils/audio';

interface GalleryGridProps {
  items: GalleryItem[];
  selectedItemIds: string[];
  onToggleSelectItem: (item: GalleryItem) => void;
  onClearSelection: () => void;
  onSelectAll: () => void;
  onUploadFiles: (files: FileList) => void;
  onDeleteItem: (id: string) => void;
  onResetSamples: () => void;
  onOpenScanner?: () => void;
}

export const GalleryGrid: React.FC<GalleryGridProps> = ({
  items,
  selectedItemIds,
  onToggleSelectItem,
  onClearSelection,
  onSelectAll,
  onUploadFiles,
  onDeleteItem,
  onResetSamples,
  onOpenScanner
}) => {
  const [filterCategory, setFilterCategory] = useState<string>('all');
  const [previewItem, setPreviewItem] = useState<GalleryItem | null>(null);
  const [isDraggingOver, setIsDraggingOver] = useState(false);
  const fileInputRef = useRef<HTMLInputElement>(null);

  const filteredItems = items.filter((item) => {
    if (filterCategory === 'all') return true;
    return item.category === filterCategory;
  });

  const handleDragOver = (e: React.DragEvent) => {
    e.preventDefault();
    setIsDraggingOver(true);
  };

  const handleDragLeave = () => {
    setIsDraggingOver(false);
  };

  const handleDrop = (e: React.DragEvent) => {
    e.preventDefault();
    setIsDraggingOver(false);
    if (e.dataTransfer.files && e.dataTransfer.files.length > 0) {
      onUploadFiles(e.dataTransfer.files);
    }
  };

  return (
    <div className="flex flex-col h-full">
      {/* Hidden File Input always mounted */}
      <input
        ref={fileInputRef}
        type="file"
        multiple
        accept="image/*,.pdf,.heic"
        className="hidden"
        onChange={(e) => {
          if (e.target.files && e.target.files.length > 0) {
            onUploadFiles(e.target.files);
            e.target.value = '';
          }
        }}
      />

      {/* Top Gallery Toolbar - Shown cleanly when items exist */}
      {items.length > 0 && (
        <>
          <div className="flex flex-wrap items-center justify-between gap-3 pb-3 border-b border-stone-800">
            {/* Left: Title & Category Filters */}
            <div className="flex items-center gap-2 flex-wrap">
              <span className="text-xs font-bold text-stone-200 font-display">
                Gallery ({items.length})
              </span>
              <div className="flex items-center gap-1 p-1 bg-stone-950/60 rounded-xl border border-stone-800/80">
                {[
                  { id: 'all', label: 'All' },
                  { id: 'document', label: 'Docs' },
                  { id: 'receipt', label: 'Receipts' },
                  { id: 'photo', label: 'Camera' },
                  { id: 'note', label: 'Notes' }
                ].map((cat) => (
                  <button
                    key={cat.id}
                    onClick={() => {
                      setFilterCategory(cat.id);
                      playHapticTick();
                    }}
                    className={`px-2.5 py-1 rounded-lg text-xs font-semibold transition-all ${
                      filterCategory === cat.id
                        ? 'bg-stone-800 text-stone-100 shadow-sm'
                        : 'text-stone-400 hover:text-stone-200 hover:bg-stone-900/50'
                    }`}
                  >
                    {cat.label}
                  </button>
                ))}
              </div>
            </div>

            {/* Action Controls: Deselect, Select All, Scan Doc, Upload */}
            <div className="flex items-center gap-2">
              {selectedItemIds.length > 0 && (
                <button
                  type="button"
                  onClick={() => {
                    onClearSelection();
                    playHapticTick();
                  }}
                  className="px-2.5 py-1.5 rounded-lg text-xs font-semibold text-stone-400 hover:text-rose-400 hover:bg-rose-950/20 transition-all border border-stone-800 cursor-pointer"
                >
                  Deselect All ({selectedItemIds.length})
                </button>
              )}

              <button
                type="button"
                onClick={() => {
                  onSelectAll();
                  playHapticTick();
                }}
                className="px-2.5 py-1.5 rounded-lg text-xs font-semibold text-stone-300 hover:text-white hover:bg-stone-800 transition-all border border-stone-800 cursor-pointer"
              >
                Select All
              </button>

              {onOpenScanner && (
                <button
                  type="button"
                  onClick={() => {
                    onOpenScanner();
                    playHapticTick();
                  }}
                  className="px-3 py-1.5 rounded-lg text-xs font-semibold text-stone-200 bg-stone-800 hover:bg-stone-700 hover:text-white transition-all border border-stone-700 flex items-center gap-1.5 cursor-pointer"
                  title="Snap physical receipts or documents with camera"
                >
                  <Camera size={14} className="text-amber-400" />
                  <span>Scan Doc</span>
                </button>
              )}

              <button
                type="button"
                onClick={() => fileInputRef.current?.click()}
                className="px-3 py-1.5 rounded-lg text-xs font-bold text-stone-950 bg-amber-400 hover:bg-amber-300 transition-all shadow-sm flex items-center gap-1.5 cursor-pointer"
              >
                <Upload size={14} />
                <span>Upload Photos / PDF</span>
              </button>
            </div>
          </div>

          {/* Tap order helper banner */}
          <div className="flex items-center justify-between text-xs text-stone-400 my-2 px-1">
            <div className="flex items-center gap-2">
              <Layers size={14} className="text-amber-500" />
              <span>
                <strong className="text-stone-200">Tap order = page sequence:</strong> Click photos in order (1, 2, 3...)
              </span>
            </div>
            <div className="tabular-nums">
              <span className="font-bold text-amber-400">{selectedItemIds.length}</span> / {items.length} selected
            </div>
          </div>
        </>
      )}

      {/* Gallery Dropzone & Grid */}
      <div
        onDragOver={handleDragOver}
        onDragLeave={handleDragLeave}
        onDrop={handleDrop}
        className={`relative flex-1 rounded-2xl border-2 transition-all p-3 overflow-y-auto ${
          isDraggingOver
            ? 'border-dashed border-amber-500 bg-amber-500/10'
            : 'border-stone-800/80 bg-stone-950/40'
        }`}
      >
        {filteredItems.length === 0 ? (
          <div
            onClick={() => fileInputRef.current?.click()}
            className="h-80 flex flex-col items-center justify-center text-center p-8 border-2 border-dashed border-stone-800 hover:border-amber-500/80 rounded-2xl bg-stone-900/30 hover:bg-stone-900/60 transition-all cursor-pointer group"
          >
            <div className="w-16 h-16 rounded-2xl bg-amber-500/10 border border-amber-500/30 flex items-center justify-center text-amber-500 mb-4 group-hover:scale-110 group-hover:bg-amber-500/20 transition-all">
              <Upload size={28} />
            </div>
            <h3 className="text-base font-bold text-stone-100 mb-1 font-display">
              Photo &amp; Document Gallery
            </h3>
            <p className="text-xs text-stone-400 mb-5 max-w-sm">
              Upload photos to convert to PDF/WebP/JPG/PNG, or upload a PDF to extract pages into JPG/PNG!
            </p>
            <div className="flex items-center gap-2.5 flex-wrap justify-center">
              <button
                type="button"
                onClick={(e) => {
                  e.stopPropagation();
                  fileInputRef.current?.click();
                }}
                className="px-5 py-2.5 rounded-xl text-xs font-bold bg-amber-500 text-stone-950 hover:bg-amber-400 transition-all shadow-md shadow-amber-500/20 active:scale-95 flex items-center gap-2 cursor-pointer"
              >
                <Plus size={16} strokeWidth={2.5} />
                <span>Upload Photos / PDF</span>
              </button>

              {onOpenScanner && (
                <button
                  type="button"
                  onClick={(e) => {
                    e.stopPropagation();
                    onOpenScanner();
                    playHapticTick();
                  }}
                  className="px-4 py-2.5 rounded-xl text-xs font-bold bg-stone-800 hover:bg-stone-700 text-stone-200 hover:text-white transition-all border border-stone-700 flex items-center gap-2 cursor-pointer"
                >
                  <Camera size={16} className="text-amber-400" />
                  <span>Scan Doc</span>
                </button>
              )}
            </div>
            <div className="text-[11px] text-stone-500 mt-3">
              Supports JPG · PNG · WEBP · HEIC · PDF
            </div>
          </div>
        ) : (
          <div className="grid grid-cols-2 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5 gap-3">
            {filteredItems.map((item) => {
              const isSelected = selectedItemIds.includes(item.id);
              const orderBadge = item.selectedOrder;

              return (
                <div
                  key={item.id}
                  onClick={() => {
                    onToggleSelectItem(item);
                    playHapticTick();
                  }}
                  className={`group relative rounded-xl overflow-hidden border cursor-pointer select-none transition-all duration-200 aspect-[4/5] flex flex-col justify-between ${
                    isSelected
                      ? 'border-amber-500 ring-2 ring-amber-500/40 shadow-lg shadow-amber-500/10 scale-[0.98]'
                      : 'border-stone-800/90 bg-stone-900/60 hover:border-stone-700 hover:shadow-md'
                  }`}
                >
                  {/* Image Thumbnail */}
                  <img
                    src={item.url}
                    alt={item.name}
                    className="absolute inset-0 w-full h-full object-cover transition-transform duration-300 group-hover:scale-105"
                    loading="lazy"
                  />

                  {/* Top Gradient Bar with Sequence Badge */}
                  <div className="relative z-10 p-2 flex items-center justify-between bg-gradient-to-b from-stone-950/80 via-stone-950/40 to-transparent">
                    {/* Numbered Tap Badge (Signature Donut Convert feature) */}
                    <div
                      className={`w-6 h-6 rounded-full flex items-center justify-center font-bold text-xs shadow-md transition-all ${
                        isSelected
                          ? 'bg-amber-500 text-stone-950 scale-110 ring-2 ring-white/60'
                          : 'bg-stone-950/70 text-stone-400 border border-stone-600/60 group-hover:border-stone-400'
                      }`}
                    >
                      {isSelected ? (
                        <span>{orderBadge}</span>
                      ) : (
                        <span className="opacity-0 group-hover:opacity-60 text-[10px]">+</span>
                      )}
                    </div>

                    {/* Quick Preview & Delete Action */}
                    <div className="flex items-center gap-1 opacity-0 group-hover:opacity-100 transition-opacity">
                      <button
                        type="button"
                        onClick={(e) => {
                          e.stopPropagation();
                          setPreviewItem(item);
                        }}
                        className="p-1 rounded-md bg-stone-950/80 text-stone-300 hover:text-white hover:bg-stone-800"
                        title="Preview Full Size"
                      >
                        <Eye size={13} />
                      </button>
                      <button
                        type="button"
                        onClick={(e) => {
                          e.stopPropagation();
                          onDeleteItem(item.id);
                        }}
                        className="p-1 rounded-md bg-stone-950/80 text-stone-400 hover:text-rose-400 hover:bg-rose-950/60"
                        title="Delete from Gallery"
                      >
                        <Trash2 size={13} />
                      </button>
                    </div>
                  </div>

                  {/* Bottom Gradient Bar with Filename and File Info */}
                  <div className="relative z-10 p-2.5 bg-gradient-to-t from-stone-950/95 via-stone-950/70 to-transparent">
                    <div className="text-xs font-semibold text-stone-100 truncate drop-shadow-sm">
                      {item.name}
                    </div>
                    <div className="flex items-center justify-between text-[10px] text-stone-400 mt-0.5">
                      <span>{item.width}×{item.height}</span>
                      <span className="tabular-nums font-mono">{formatBytes(item.sizeBytes)}</span>
                    </div>
                  </div>
                </div>
              );
            })}

            {/* Quick Add More Tile */}
            <div
              onClick={() => fileInputRef.current?.click()}
              className="rounded-xl border-2 border-dashed border-stone-800 hover:border-amber-500/80 bg-stone-900/30 hover:bg-stone-900/60 aspect-[4/5] flex flex-col items-center justify-center p-3 text-center cursor-pointer transition-all group select-none"
            >
              <div className="w-10 h-10 rounded-full bg-stone-800 group-hover:bg-amber-500/20 text-stone-400 group-hover:text-amber-400 flex items-center justify-center mb-2 transition-all">
                <Plus size={20} />
              </div>
              <div className="text-xs font-bold text-stone-300 group-hover:text-white">
                Add More
              </div>
              <div className="text-[10px] text-stone-500">
                Photos / PDF
              </div>
            </div>
          </div>
        )}
      </div>

      {/* Lightbox Preview Modal */}
      <AnimatePresence>
        {previewItem && (
          <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-stone-950/90 backdrop-blur-md">
            <motion.div
              initial={{ scale: 0.95, opacity: 0 }}
              animate={{ scale: 1, opacity: 1 }}
              exit={{ scale: 0.95, opacity: 0 }}
              className="relative max-w-4xl max-h-[90vh] bg-stone-900 border border-stone-800 rounded-2xl overflow-hidden flex flex-col shadow-2xl"
            >
              {/* Preview Header */}
              <div className="flex items-center justify-between px-4 py-3 border-b border-stone-800">
                <div className="text-xs font-semibold text-stone-200 truncate pr-4">
                  {previewItem.name} ({previewItem.width} × {previewItem.height} · {formatBytes(previewItem.sizeBytes)})
                </div>
                <button
                  onClick={() => setPreviewItem(null)}
                  className="px-2.5 py-1 rounded-lg bg-stone-800 text-stone-300 hover:text-white text-xs font-bold"
                >
                  Close
                </button>
              </div>

              {/* Preview Body */}
              <div className="p-4 flex items-center justify-center overflow-auto bg-stone-950/60">
                <img
                  src={previewItem.url}
                  alt={previewItem.name}
                  className="max-h-[75vh] w-auto object-contain rounded shadow-lg"
                />
              </div>
            </motion.div>
          </div>
        )}
      </AnimatePresence>
    </div>
  );
};
