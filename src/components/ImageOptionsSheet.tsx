import React, { useState, useEffect } from 'react';
import { motion } from 'motion/react';
import { X, Image, Archive, Sparkles } from 'lucide-react';
import { GalleryItem, ImageOptions } from '../types';
import { formatBytes } from '../utils/converter';

interface ImageOptionsSheetProps {
  isOpen: boolean;
  onClose: () => void;
  format: 'jpg' | 'png' | 'webp';
  selectedItems: GalleryItem[];
  onStartConversion: (options: ImageOptions) => void;
}

const STORAGE_KEY_IMG = 'donut_convert_img_prefs_v1';

export const ImageOptionsSheet: React.FC<ImageOptionsSheetProps> = ({
  isOpen,
  onClose,
  format,
  selectedItems,
  onStartConversion
}) => {
  const [quality, setQuality] = useState<number>(format === 'png' ? 1.0 : 0.85);
  const [scale, setScale] = useState<number>(1.0);
  const [filenamePattern, setFilenamePattern] = useState<string>(`Donut_${format.toUpperCase()}_{date}`);
  const [packageAsZip, setPackageAsZip] = useState<boolean>(selectedItems.length > 1);
  const [targetSizeKb, setTargetSizeKb] = useState<number | undefined>(undefined);
  const [rememberChoice, setRememberChoice] = useState<boolean>(true);

  useEffect(() => {
    try {
      const saved = localStorage.getItem(STORAGE_KEY_IMG);
      if (saved) {
        const parsed = JSON.parse(saved);
        if (typeof parsed.quality === 'number' && format !== 'png') setQuality(parsed.quality);
        if (typeof parsed.scale === 'number') setScale(parsed.scale);
        if (typeof parsed.packageAsZip === 'boolean') setPackageAsZip(parsed.packageAsZip);
        setRememberChoice(true);
      }
    } catch {
      // ignore
    }
  }, [format]);

  if (!isOpen) return null;

  const handleConvert = () => {
    const opts: ImageOptions = {
      format,
      quality,
      scale,
      filenamePattern: filenamePattern.trim() || `Donut_${format.toUpperCase()}_{date}`,
      packageAsZip,
      rememberChoice,
      targetSizeKb
    };

    if (rememberChoice) {
      try {
        localStorage.setItem(STORAGE_KEY_IMG, JSON.stringify(opts));
      } catch {
        // ignore
      }
    }

    onStartConversion(opts);
  };

  const formatColor =
    format === 'jpg' ? 'emerald' : format === 'png' ? 'violet' : 'sky';

  const totalRawBytes = selectedItems.reduce((acc, it) => acc + it.sizeBytes, 0);

  return (
    <div className="fixed inset-0 z-50 flex items-end sm:items-center justify-center p-0 sm:p-4 bg-stone-950/80 backdrop-blur-sm animate-fade-in">
      <motion.div
        initial={{ y: 80, opacity: 0 }}
        animate={{ y: 0, opacity: 1 }}
        exit={{ y: 80, opacity: 0 }}
        transition={{ duration: 0.22, ease: [0.16, 1, 0.3, 1] }}
        className="w-full max-w-lg bg-stone-900 border border-stone-800 rounded-t-3xl sm:rounded-3xl shadow-2xl flex flex-col max-h-[90vh] overflow-hidden"
      >
        {/* Grab handle */}
        <div className="w-12 h-1.5 bg-stone-700 rounded-full mx-auto mt-3 sm:hidden" />

        {/* Header */}
        <div className="flex items-center justify-between px-6 py-4 border-b border-stone-800">
          <div className="flex items-center gap-3">
            <div className={`w-9 h-9 rounded-xl flex items-center justify-center font-bold text-xs uppercase ${
              format === 'jpg'
                ? 'bg-emerald-500/10 text-emerald-400 border border-emerald-500/30'
                : format === 'png'
                ? 'bg-violet-500/10 text-violet-400 border border-violet-500/30'
                : 'bg-sky-500/10 text-sky-400 border border-sky-500/30'
            }`}>
              {format}
            </div>
            <div>
              <h2 className="text-lg font-bold font-display text-white">
                Convert to {format.toUpperCase()}
              </h2>
              <div className="text-xs text-stone-400">
                {selectedItems.length} {selectedItems.length === 1 ? 'image' : 'images'} selected · {formatBytes(totalRawBytes)} total
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

        {/* Body */}
        <div className="p-6 overflow-y-auto space-y-5 flex-1">
          {/* Quality slider (not applicable to standard lossless PNG) */}
          {format !== 'png' ? (
            <div>
              <div className="flex items-center justify-between mb-2">
                <label className="text-xs font-bold text-stone-300 uppercase tracking-wider">
                  Compression Quality
                </label>
                <span className="text-xs font-bold text-stone-200 tabular-nums">
                  {Math.round(quality * 100)}%
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
                <span>Smaller file size</span>
                <span>Crisp photo quality</span>
              </div>
            </div>
          ) : (
            <div className="p-3 bg-violet-950/20 border border-violet-800/40 rounded-xl text-xs text-violet-300 flex items-center gap-2">
              <Sparkles size={16} className="text-violet-400 shrink-0" />
              <span>PNG encoding preserves 100% lossless fidelity and alpha transparency.</span>
            </div>
          )}

          {/* Scale Resolution Factor */}
          <div>
            <label className="text-xs font-bold text-stone-300 uppercase tracking-wider block mb-2">
              Resolution Scale
            </label>
            <div className="grid grid-cols-4 gap-2">
              {[
                { factor: 0.5, label: '50% (Eco)' },
                { factor: 0.75, label: '75%' },
                { factor: 1.0, label: '100% (Original)' },
                { factor: 1.5, label: '150% (Upscale)' }
              ].map((s) => (
                <button
                  key={s.factor}
                  type="button"
                  onClick={() => setScale(s.factor)}
                  className={`py-2 px-2.5 rounded-lg text-xs font-semibold border transition-all text-center ${
                    scale === s.factor
                      ? 'bg-amber-500 text-stone-950 border-amber-500'
                      : 'bg-stone-800/60 text-stone-400 border-stone-700/60 hover:text-stone-200'
                  }`}
                >
                  {s.label}
                </button>
              ))}
            </div>
          </div>

          {/* Package as ZIP toggle when multiple selected */}
          {selectedItems.length > 1 && (
            <div className="p-3.5 bg-stone-800/40 border border-stone-700/60 rounded-xl">
              <label className="flex items-center justify-between cursor-pointer">
                <div className="flex items-center gap-2.5">
                  <Archive size={18} className="text-stone-400" />
                  <div>
                    <div className="text-xs font-bold text-stone-200">Package into .ZIP file</div>
                    <div className="text-[11px] text-stone-400">
                      Bundles all {selectedItems.length} converted images into a single clean download
                    </div>
                  </div>
                </div>
                <input
                  type="checkbox"
                  checked={packageAsZip}
                  onChange={(e) => setPackageAsZip(e.target.checked)}
                  className="rounded border-stone-700 text-amber-500 focus:ring-amber-500 bg-stone-800 w-4 h-4"
                />
              </label>
            </div>
          )}

          {/* Filename pattern */}
          <div>
            <label className="text-xs font-bold text-stone-300 uppercase tracking-wider block mb-2">
              File Name Template
            </label>
            <div className="flex items-center gap-2">
              <input
                type="text"
                value={filenamePattern}
                onChange={(e) => setFilenamePattern(e.target.value)}
                placeholder={`Donut_${format.toUpperCase()}_{date}`}
                className="flex-1 px-3.5 py-2 rounded-xl bg-stone-800 border border-stone-700 text-stone-100 text-sm font-mono focus:outline-none focus:border-amber-500"
              />
              <span className="text-xs text-stone-500 font-mono">.{format}</span>
            </div>
          </div>

          {/* Target File Size (Optional) */}
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
                      ? 'bg-amber-500 text-stone-950 border-amber-500 font-bold shadow-sm'
                      : 'bg-stone-800/60 text-stone-400 border-stone-700/60 hover:text-stone-200'
                  }`}
                >
                  {preset.label}
                </button>
              ))}
            </div>
          </div>

          {/* Remember my choice */}
          <div className="pt-2 border-t border-stone-800/80">
            <label className="flex items-center gap-2.5 cursor-pointer text-xs text-stone-300">
              <input
                type="checkbox"
                checked={rememberChoice}
                onChange={(e) => setRememberChoice(e.target.checked)}
                className="rounded border-stone-700 text-amber-500 focus:ring-amber-500 bg-stone-800"
              />
              <span>Remember settings for {format.toUpperCase()} conversions</span>
            </label>
          </div>
        </div>

        {/* Footer */}
        <div className="p-4 sm:p-6 border-t border-stone-800 bg-stone-900/90 flex items-center justify-end gap-3">
          <button
            type="button"
            onClick={onClose}
            className="px-4 py-2.5 rounded-xl border border-stone-700 text-stone-300 text-xs font-bold hover:bg-stone-800 transition-colors"
          >
            Cancel
          </button>

          <button
            type="button"
            onClick={handleConvert}
            className={`px-6 py-2.5 rounded-xl text-stone-950 text-xs font-bold transition-all shadow-lg active:scale-[0.98] flex items-center gap-2 ${
              format === 'jpg'
                ? 'bg-emerald-400 hover:bg-emerald-300 shadow-emerald-500/20'
                : format === 'png'
                ? 'bg-violet-400 hover:bg-violet-300 shadow-violet-500/20'
                : 'bg-sky-400 hover:bg-sky-300 shadow-sky-500/20'
            }`}
          >
            <Image size={16} />
            <span>Convert to {format.toUpperCase()}</span>
          </button>
        </div>
      </motion.div>
    </div>
  );
};
