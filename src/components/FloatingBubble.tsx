import React, { useState, useRef, useEffect } from 'react';
import { motion, AnimatePresence } from 'motion/react';
import { Sparkles, X, FileText, Image, Zap, Layers, Upload } from 'lucide-react';
import { FormatType, GalleryItem } from '../types';
import { playHapticTick, playSectorSelect } from '../utils/audio';

interface FloatingBubbleProps {
  selectedCount: number;
  onSelectFormat: (format: FormatType) => void;
  onUploadFiles?: (files: FileList) => void;
  isOpen: boolean;
  onToggleBubble: () => void;
}

export const FloatingBubble: React.FC<FloatingBubbleProps> = ({
  selectedCount,
  onSelectFormat,
  onUploadFiles,
  isOpen,
  onToggleBubble
}) => {
  const [position, setPosition] = useState({ x: 24, y: 180 });
  const [isExpanded, setIsExpanded] = useState(false);
  const [isDragging, setIsDragging] = useState(false);
  const dragStartRef = useRef({ startX: 0, startY: 0, initialX: 0, initialY: 0 });
  const hasMovedRef = useRef(false);
  const fileInputRef = useRef<HTMLInputElement>(null);

  if (!isOpen) return null;

  const handlePointerDown = (e: React.PointerEvent) => {
    setIsDragging(true);
    hasMovedRef.current = false;
    dragStartRef.current = {
      startX: e.clientX,
      startY: e.clientY,
      initialX: position.x,
      initialY: position.y
    };
    (e.target as HTMLElement).setPointerCapture(e.pointerId);
  };

  const handlePointerMove = (e: React.PointerEvent) => {
    if (!isDragging) return;
    const dx = e.clientX - dragStartRef.current.startX;
    const dy = e.clientY - dragStartRef.current.startY;

    if (Math.abs(dx) > 4 || Math.abs(dy) > 4) {
      hasMovedRef.current = true;
    }

    const newX = Math.max(10, Math.min(window.innerWidth - 70, dragStartRef.current.initialX - dx));
    const newY = Math.max(70, Math.min(window.innerHeight - 80, dragStartRef.current.initialY + dy));
    setPosition({ x: newX, y: newY });
  };

  const handlePointerUp = (e: React.PointerEvent) => {
    setIsDragging(false);
    try {
      (e.target as HTMLElement).releasePointerCapture(e.pointerId);
    } catch {
      // ignore
    }

    if (!hasMovedRef.current) {
      // It was a tap/click!
      setIsExpanded(!isExpanded);
      playHapticTick();
    }
  };

  return (
    <>
      <div
        style={{
          right: `${position.x}px`,
          top: `${position.y}px`
        }}
        className="fixed z-50 select-none touch-none"
      >
        {/* Floating Bubble Circle */}
        <motion.div
          onPointerDown={handlePointerDown}
          onPointerMove={handlePointerMove}
          onPointerUp={handlePointerUp}
          whileHover={{ scale: 1.08 }}
          whileTap={{ scale: 0.94 }}
          className={`relative w-14 h-14 rounded-full flex items-center justify-center cursor-grab active:cursor-grabbing shadow-2xl transition-all ${
            isExpanded
              ? 'bg-amber-500 text-stone-950 ring-4 ring-amber-500/30'
              : 'bg-stone-900 border-2 border-amber-500/80 text-amber-400 hover:border-amber-400'
          }`}
        >
          {/* Donut graphic inside bubble */}
          <div className="relative w-8 h-8 rounded-full border-4 border-amber-400 flex items-center justify-center shadow-inner">
            <div className="w-2.5 h-2.5 rounded-full bg-stone-900" />
            {/* Small sprinkle dot */}
            <span className="absolute top-0.5 right-1 w-1 h-1 rounded-full bg-rose-400" />
            <span className="absolute bottom-1 left-0.5 w-1 h-1 rounded-full bg-sky-300" />
          </div>

          {/* Badge count of selected items */}
          {selectedCount > 0 && (
            <span className="absolute -top-1 -right-1 w-5 h-5 bg-rose-500 text-white rounded-full font-bold text-[10px] flex items-center justify-center border-2 border-stone-900 shadow">
              {selectedCount}
            </span>
          )}
        </motion.div>

        {/* Expanded Floating Menu */}
        <AnimatePresence>
          {isExpanded && (
            <motion.div
              initial={{ scale: 0.85, opacity: 0, y: -10 }}
              animate={{ scale: 1, opacity: 1, y: 0 }}
              exit={{ scale: 0.85, opacity: 0, y: -10 }}
              transition={{ duration: 0.16 }}
              className="absolute right-0 mt-3 w-64 bg-stone-900/95 border border-stone-800 rounded-2xl p-4 shadow-2xl backdrop-blur-md"
            >
              {/* Header */}
              <div className="flex items-center justify-between pb-2 mb-2 border-b border-stone-800">
                <div className="flex items-center gap-1.5">
                  <div className="w-2 h-2 rounded-full bg-amber-500" />
                  <span className="text-xs font-bold font-display text-white">Donut Quick Convert</span>
                </div>
                <button
                  onClick={() => setIsExpanded(false)}
                  className="p-1 text-stone-400 hover:text-white rounded"
                >
                  <X size={14} />
                </button>
              </div>

              {/* Status */}
              <div className="text-[11px] text-stone-400 mb-3">
                {selectedCount > 0 ? (
                  <span>Ready to convert <strong className="text-amber-400">{selectedCount} {selectedCount === 1 ? 'item' : 'items'}</strong></span>
                ) : (
                  <span>Select gallery items or upload photos below</span>
                )}
              </div>

              {/* Quick Format Buttons */}
              <div className="grid grid-cols-2 gap-2 mb-3">
                {[
                  { id: 'pdf', label: 'PDF', icon: FileText, color: 'hover:border-amber-500 hover:text-amber-400' },
                  { id: 'webp', label: 'WEBP', icon: Zap, color: 'hover:border-sky-500 hover:text-sky-400' },
                  { id: 'jpg', label: 'JPG', icon: Image, color: 'hover:border-emerald-500 hover:text-emerald-400' },
                  { id: 'png', label: 'PNG', icon: Sparkles, color: 'hover:border-violet-500 hover:text-violet-400' }
                ].map((fmt) => (
                  <button
                    key={fmt.id}
                    onClick={() => {
                      playSectorSelect();
                      setIsExpanded(false);
                      onSelectFormat(fmt.id as FormatType);
                    }}
                    className={`p-2.5 rounded-xl border border-stone-800 bg-stone-800/40 text-xs font-bold text-stone-200 flex items-center gap-2 transition-all cursor-pointer ${fmt.color}`}
                  >
                    <fmt.icon size={14} />
                    <span>{fmt.label}</span>
                  </button>
                ))}
              </div>

              {/* Upload directly to bubble */}
              {onUploadFiles && (
                <button
                  onClick={() => fileInputRef.current?.click()}
                  className="w-full py-2 px-3 rounded-xl border border-stone-800 bg-stone-950/60 text-stone-300 hover:text-white text-xs font-semibold flex items-center justify-center gap-1.5 transition-colors"
                >
                  <Upload size={13} />
                  <span>Drop / Select New File</span>
                </button>
              )}
              <input
                ref={fileInputRef}
                type="file"
                multiple
                accept="image/*"
                className="hidden"
                onChange={(e) => {
                  if (e.target.files && onUploadFiles) {
                    onUploadFiles(e.target.files);
                    setIsExpanded(false);
                  }
                }}
              />

              <div className="mt-3 pt-2 border-t border-stone-800/60 flex items-center justify-between text-[10px] text-stone-500">
                <span>Floating Bubble Service</span>
                <button
                  onClick={onToggleBubble}
                  className="text-stone-400 hover:text-rose-400 transition-colors"
                >
                  Hide Bubble
                </button>
              </div>
            </motion.div>
          )}
        </AnimatePresence>
      </div>
    </>
  );
};
