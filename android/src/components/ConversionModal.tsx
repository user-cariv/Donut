import React, { useEffect } from 'react';
import { motion, AnimatePresence } from 'motion/react';
import confetti from 'canvas-confetti';
import {
  CheckCircle2,
  Download,
  AlertCircle,
  FileText,
  Image as ImageIcon,
  Archive,
  ArrowRight,
  ExternalLink,
  RefreshCw,
  Share2
} from 'lucide-react';
import { ConvertedFile, ConversionProgress } from '../types';
import { formatBytes, downloadFile, shareConvertedFile } from '../utils/converter';
import { playSuccessChime } from '../utils/audio';

interface ConversionModalProps {
  isOpen: boolean;
  onClose: () => void;
  progress: ConversionProgress;
  results: ConvertedFile[];
  onOpenHistory: () => void;
}

export const ConversionModal: React.FC<ConversionModalProps> = ({
  isOpen,
  onClose,
  progress,
  results,
  onOpenHistory
}) => {
  useEffect(() => {
    if (progress.status === 'completed' && results.length > 0) {
      playSuccessChime();
      try {
        confetti({
          particleCount: 70,
          spread: 60,
          origin: { y: 0.65 },
          colors: ['#f59e0b', '#ea580c', '#10b981', '#38bdf8', '#8b5cf6']
        });
      } catch {
        // ignore
      }
    }
  }, [progress.status, results.length]);

  if (!isOpen) return null;

  const totalOrigSize = results.reduce((a, b) => a + b.originalSizeBytes, 0);
  const totalConvSize = results.reduce((a, b) => a + b.convertedSizeBytes, 0);
  const sizeDiff = totalOrigSize - totalConvSize;
  const percentReduction = totalOrigSize > 0 ? Math.round((sizeDiff / totalOrigSize) * 100) : 0;

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-stone-950/85 backdrop-blur-md animate-fade-in">
      <motion.div
        initial={{ scale: 0.92, opacity: 0 }}
        animate={{ scale: 1, opacity: 1 }}
        exit={{ scale: 0.92, opacity: 0 }}
        className="w-full max-w-lg bg-stone-900 border border-stone-800 rounded-3xl p-6 shadow-2xl overflow-hidden relative"
      >
        {/* Glow ambient background */}
        <div className="absolute top-0 right-0 w-64 h-64 bg-amber-500/10 rounded-full blur-3xl pointer-events-none" />

        {progress.status === 'processing' && (
          <div className="text-center py-6">
            <div className="w-16 h-16 rounded-full bg-amber-500/10 border-2 border-amber-500/40 flex items-center justify-center mx-auto mb-4 animate-spin">
              <RefreshCw size={28} className="text-amber-500" />
            </div>

            <h3 className="text-lg font-bold font-display text-white mb-1">
              Converting with Donut Engine...
            </h3>
            <p className="text-xs text-stone-400 mb-6 truncate max-w-xs mx-auto">
              {progress.currentFilename || `Processing page ${progress.currentPage} of ${progress.totalPages}...`}
            </p>

            {/* Progress bar */}
            <div className="w-full bg-stone-800 rounded-full h-3 overflow-hidden border border-stone-700/60 p-0.5 mb-2">
              <motion.div
                className="bg-gradient-to-r from-amber-500 via-orange-500 to-amber-400 h-full rounded-full"
                initial={{ width: 0 }}
                animate={{ width: `${progress.percent}%` }}
                transition={{ duration: 0.2 }}
              />
            </div>

            <div className="flex items-center justify-between text-xs text-stone-400">
              <span>Streaming bitmap buffer</span>
              <span className="font-bold text-amber-400 tabular-nums">{progress.percent}%</span>
            </div>
          </div>
        )}

        {progress.status === 'completed' && (
          <div>
            {/* Header */}
            <div className="flex items-center gap-3 mb-5">
              <div className="w-12 h-12 rounded-2xl bg-emerald-500/10 border border-emerald-500/30 flex items-center justify-center text-emerald-400 shrink-0">
                <CheckCircle2 size={26} />
              </div>
              <div>
                <h3 className="text-lg font-bold font-display text-white">Conversion Complete!</h3>
                <div className="text-xs text-stone-400">
                  {results.length} {results.length === 1 ? 'file' : 'files'} generated safely
                </div>
              </div>
            </div>

            {/* Savings & Metrics Banner */}
            <div className="grid grid-cols-3 gap-2 p-3 bg-stone-950/60 border border-stone-800/80 rounded-2xl mb-4 text-center">
              <div>
                <div className="text-[11px] text-stone-400">Original Size</div>
                <div className="text-xs font-bold text-stone-300 font-mono mt-0.5">
                  {formatBytes(totalOrigSize)}
                </div>
              </div>
              <div>
                <div className="text-[11px] text-stone-400">Output Size</div>
                <div className="text-xs font-bold text-amber-400 font-mono mt-0.5">
                  {formatBytes(totalConvSize)}
                </div>
              </div>
              <div>
                <div className="text-[11px] text-stone-400">Efficiency</div>
                <div className="text-xs font-bold text-emerald-400 font-mono mt-0.5">
                  {percentReduction > 0 ? `-${percentReduction}%` : 'Lossless'}
                </div>
              </div>
            </div>

            {/* Result Files List */}
            <div className="max-h-52 overflow-y-auto space-y-2 mb-5 pr-1">
              {results.map((file) => (
                <div
                  key={file.id}
                  className="flex items-center justify-between p-3 bg-stone-800/50 border border-stone-700/60 rounded-xl"
                >
                  <div className="flex items-center gap-3 min-w-0">
                    <div className="w-8 h-8 rounded-lg bg-stone-900 border border-stone-700 flex items-center justify-center font-bold text-[10px] uppercase text-amber-400 shrink-0">
                      {file.format}
                    </div>
                    <div className="min-w-0">
                      <div className="text-xs font-semibold text-stone-100 truncate">
                        {file.filename}
                      </div>
                      <div className="text-[11px] text-stone-400 flex items-center gap-2">
                        <span>{formatBytes(file.convertedSizeBytes)}</span>
                        {file.pageCount && file.pageCount > 1 && (
                          <span>· {file.pageCount} pages</span>
                        )}
                      </div>
                    </div>
                  </div>

                  <div className="flex items-center gap-1.5 shrink-0">
                    <button
                      type="button"
                      onClick={() => shareConvertedFile(file)}
                      className="p-2 rounded-lg bg-stone-800 hover:bg-stone-700 text-stone-200 transition-colors shadow-sm cursor-pointer"
                      title="Share to WhatsApp / Android Apps"
                    >
                      <Share2 size={15} />
                    </button>
                    <button
                      type="button"
                      onClick={() => downloadFile(file)}
                      className="p-2 rounded-lg bg-amber-500 hover:bg-amber-400 text-stone-950 transition-colors shadow-sm cursor-pointer"
                      title="Download this file"
                    >
                      <Download size={15} strokeWidth={2.5} />
                    </button>
                  </div>
                </div>
              ))}
            </div>

            {/* Action Buttons */}
            <div className="flex flex-col sm:flex-row items-center gap-2">
              <button
                type="button"
                onClick={() => {
                  results.forEach((f) => downloadFile(f));
                }}
                className="w-full sm:flex-1 py-3 px-4 rounded-xl bg-amber-500 hover:bg-amber-400 text-stone-950 text-xs font-bold transition-all shadow-lg shadow-amber-500/20 active:scale-[0.98] flex items-center justify-center gap-2 cursor-pointer"
              >
                <Download size={16} strokeWidth={2.5} />
                <span>Download All ({results.length})</span>
              </button>

              {results.length > 0 && (
                <button
                  type="button"
                  onClick={() => shareConvertedFile(results[0])}
                  className="w-full sm:w-auto py-3 px-4 rounded-xl border border-stone-700 bg-stone-800 hover:bg-stone-700 text-stone-200 text-xs font-bold transition-colors flex items-center justify-center gap-1.5 cursor-pointer"
                  title="Share first file or ZIP"
                >
                  <Share2 size={15} />
                  <span>Share</span>
                </button>
              )}

              <button
                type="button"
                onClick={() => {
                  onClose();
                  onOpenHistory();
                }}
                className="w-full sm:w-auto py-3 px-4 rounded-xl border border-stone-700 bg-stone-800 hover:bg-stone-700 text-stone-200 text-xs font-bold transition-colors flex items-center justify-center gap-1.5 cursor-pointer"
              >
                <span>View Library</span>
                <ArrowRight size={14} />
              </button>
            </div>
          </div>
        )}

        {progress.status === 'error' && (
          <div className="text-center py-6">
            <AlertCircle size={40} className="text-rose-500 mx-auto mb-3" />
            <h3 className="text-base font-bold text-white mb-1">Conversion Failed</h3>
            <p className="text-xs text-rose-300 mb-4">{progress.error || 'An unexpected error occurred.'}</p>
            <button
              onClick={onClose}
              className="px-4 py-2 bg-stone-800 text-stone-200 rounded-xl text-xs font-bold hover:bg-stone-700"
            >
              Dismiss
            </button>
          </div>
        )}
      </motion.div>
    </div>
  );
};
