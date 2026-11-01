import React, { useState, useRef, useEffect } from 'react';
import {
  Camera,
  X,
  Sparkles,
  RotateCw,
  Check,
  Plus,
  Sliders,
  FileText,
  AlertCircle
} from 'lucide-react';
import { GalleryItem } from '../types';
import { playHapticTick, playSuccessChime } from '../utils/audio';

interface CameraScannerModalProps {
  isOpen: boolean;
  onClose: () => void;
  onAddScannedItems: (items: GalleryItem[]) => void;
}

type ScanFilter = 'color' | 'enhance' | 'bw' | 'grayscale';

export const CameraScannerModal: React.FC<CameraScannerModalProps> = ({
  isOpen,
  onClose,
  onAddScannedItems
}) => {
  const [stream, setStream] = useState<MediaStream | null>(null);
  const [cameraError, setCameraError] = useState<string | null>(null);
  const [capturedPages, setCapturedPages] = useState<{ id: string; url: string; filter: ScanFilter }[]>([]);
  const [activeFilter, setActiveFilter] = useState<ScanFilter>('enhance');
  const [flashActive, setFlashActive] = useState<boolean>(false);

  const videoRef = useRef<HTMLVideoElement>(null);
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const fileFallbackRef = useRef<HTMLInputElement>(null);

  // Initialize camera stream when opened
  useEffect(() => {
    let currentStream: MediaStream | null = null;

    if (isOpen) {
      setCameraError(null);
      navigator.mediaDevices
        ?.getUserMedia({
          video: {
            facingMode: 'environment', // Prefer back camera on phones
            width: { ideal: 1920 },
            height: { ideal: 1080 }
          },
          audio: false
        })
        .then((s) => {
          currentStream = s;
          setStream(s);
          if (videoRef.current) {
            videoRef.current.srcObject = s;
            videoRef.current.play().catch(() => {});
          }
        })
        .catch((err) => {
          console.warn('Camera access error:', err);
          setCameraError('Camera access unavailable or declined. You can take a photo using your device camera.');
        });
    }

    return () => {
      if (currentStream) {
        currentStream.getTracks().forEach((track) => track.stop());
      }
    };
  }, [isOpen]);

  if (!isOpen) return null;

  // Apply visual document filters onto canvas
  const processDocumentFilter = (sourceCanvas: HTMLCanvasElement, filter: ScanFilter): string => {
    const outputCanvas = document.createElement('canvas');
    outputCanvas.width = sourceCanvas.width;
    outputCanvas.height = sourceCanvas.height;
    const ctx = outputCanvas.getContext('2d');
    if (!ctx) return sourceCanvas.toDataURL('image/jpeg', 0.9);

    ctx.drawImage(sourceCanvas, 0, 0);

    if (filter === 'color') {
      return outputCanvas.toDataURL('image/jpeg', 0.9);
    }

    const imgData = ctx.getImageData(0, 0, outputCanvas.width, outputCanvas.height);
    const data = imgData.data;

    for (let i = 0; i < data.length; i += 4) {
      const r = data[i];
      const g = data[i + 1];
      const b = data[i + 2];
      const gray = 0.299 * r + 0.587 * g + 0.114 * b;

      if (filter === 'grayscale') {
        data[i] = gray;
        data[i + 1] = gray;
        data[i + 2] = gray;
      } else if (filter === 'bw') {
        // High-contrast document threshold (crisp black text on clean white)
        const v = gray > 135 ? 255 : 0;
        data[i] = v;
        data[i + 1] = v;
        data[i + 2] = v;
      } else if (filter === 'enhance') {
        // Auto-Enhance contrast curve
        const factor = 1.35;
        data[i] = Math.min(255, Math.max(0, (r - 128) * factor + 128));
        data[i + 1] = Math.min(255, Math.max(0, (g - 128) * factor + 128));
        data[i + 2] = Math.min(255, Math.max(0, (b - 128) * factor + 128));
      }
    }

    ctx.putImageData(imgData, 0, 0);
    return outputCanvas.toDataURL('image/jpeg', 0.9);
  };

  const handleCapturePhoto = () => {
    playHapticTick();
    setFlashActive(true);
    setTimeout(() => setFlashActive(false), 150);

    if (videoRef.current && canvasRef.current) {
      const video = videoRef.current;
      const canvas = canvasRef.current;
      canvas.width = video.videoWidth || 1280;
      canvas.height = video.videoHeight || 720;
      const ctx = canvas.getContext('2d');
      if (ctx) {
        ctx.drawImage(video, 0, 0, canvas.width, canvas.height);
        const filteredUrl = processDocumentFilter(canvas, activeFilter);
        setCapturedPages((prev) => [
          ...prev,
          {
            id: `snap-${Date.now()}-${prev.length + 1}`,
            url: filteredUrl,
            filter: activeFilter
          }
        ]);
        playSuccessChime();
      }
    }
  };

  const handleFallbackFileCapture = (e: React.ChangeEvent<HTMLInputElement>) => {
    if (e.target.files && e.target.files[0]) {
      const file = e.target.files[0];
      const reader = new FileReader();
      reader.onload = (ev) => {
        const url = ev.target?.result as string;
        setCapturedPages((prev) => [
          ...prev,
          {
            id: `snap-${Date.now()}-${prev.length + 1}`,
            url,
            filter: 'enhance'
          }
        ]);
        playSuccessChime();
      };
      reader.readAsDataURL(file);
    }
  };

  const handleFinishScan = () => {
    if (capturedPages.length === 0) {
      onClose();
      return;
    }

    const newItems: GalleryItem[] = capturedPages.map((page, idx) => ({
      id: `scanned-${Date.now()}-${idx}`,
      name: `Scan_Doc_${String(idx + 1).padStart(2, '0')}.jpg`,
      url: page.url,
      type: 'image/jpeg',
      sizeBytes: Math.round((page.url.length * 3) / 4),
      width: 1280,
      height: 720,
      dateAdded: 'Just now',
      category: 'document',
      selectedOrder: idx + 1
    }));

    onAddScannedItems(newItems);
    onClose();
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-3 sm:p-5 bg-stone-950/90 backdrop-blur-md animate-fade-in">
      <div className="w-full max-w-xl bg-stone-900 border border-stone-800 rounded-3xl shadow-2xl flex flex-col overflow-hidden max-h-[92vh]">
        {/* Header */}
        <div className="p-4 border-b border-stone-800 flex items-center justify-between">
          <div className="flex items-center gap-2.5">
            <div className="w-8 h-8 rounded-xl bg-amber-500/10 border border-amber-500/30 flex items-center justify-center text-amber-500">
              <Camera size={18} />
            </div>
            <div>
              <h2 className="text-sm font-bold text-white font-display">
                Camera Document Scanner
              </h2>
              <p className="text-[11px] text-stone-400">
                Snap physical documents, receipts, or notes
              </p>
            </div>
          </div>

          <button
            onClick={onClose}
            className="p-2 text-stone-400 hover:text-white rounded-lg hover:bg-stone-800 transition-colors"
          >
            <X size={18} />
          </button>
        </div>

        {/* Viewfinder or Fallback */}
        <div className="relative flex-1 bg-stone-950 min-h-[320px] flex items-center justify-center overflow-hidden">
          {cameraError ? (
            <div className="p-6 text-center max-w-sm">
              <AlertCircle size={32} className="text-amber-500 mx-auto mb-3" />
              <p className="text-xs text-stone-300 mb-4">{cameraError}</p>
              <button
                type="button"
                onClick={() => fileFallbackRef.current?.click()}
                className="px-4 py-2 bg-amber-500 text-stone-950 font-bold text-xs rounded-xl shadow-md hover:bg-amber-400 flex items-center gap-2 mx-auto cursor-pointer"
              >
                <Camera size={16} />
                <span>Snap with Device Camera</span>
              </button>
            </div>
          ) : (
            <>
              {/* Live Video Stream */}
              <video
                ref={videoRef}
                playsInline
                muted
                className="w-full h-full object-cover max-h-[380px]"
              />

              {/* Viewfinder Target Framing Reticle */}
              <div className="absolute inset-8 pointer-events-none border border-amber-500/40 rounded-2xl flex flex-col justify-between p-2">
                <div className="flex justify-between">
                  <div className="w-6 h-6 border-t-2 border-l-2 border-amber-400 rounded-tl-lg" />
                  <div className="w-6 h-6 border-t-2 border-r-2 border-amber-400 rounded-tr-lg" />
                </div>
                <div className="text-center">
                  <span className="text-[10px] uppercase font-bold tracking-wider text-amber-400/80 bg-stone-950/70 px-2.5 py-1 rounded-full backdrop-blur-sm">
                    Align Document Within Frame
                  </span>
                </div>
                <div className="flex justify-between">
                  <div className="w-6 h-6 border-b-2 border-l-2 border-amber-400 rounded-bl-lg" />
                  <div className="w-6 h-6 border-b-2 border-r-2 border-amber-400 rounded-br-lg" />
                </div>
              </div>

              {/* Shutter flash effect */}
              {flashActive && (
                <div className="absolute inset-0 bg-white pointer-events-none opacity-80 transition-opacity duration-150" />
              )}
            </>
          )}

          {/* Hidden Canvas for Frame Extraction */}
          <canvas ref={canvasRef} className="hidden" />
          <input
            ref={fileFallbackRef}
            type="file"
            accept="image/*"
            capture="environment"
            className="hidden"
            onChange={handleFallbackFileCapture}
          />
        </div>

        {/* Filter Selection Bar */}
        <div className="px-4 py-2.5 bg-stone-900 border-t border-stone-800 flex items-center justify-between text-xs">
          <div className="flex items-center gap-1.5">
            <Sliders size={14} className="text-stone-400" />
            <span className="text-stone-400 font-semibold text-[11px]">Filter:</span>
          </div>

          <div className="flex items-center gap-1">
            {[
              { id: 'enhance', label: 'Magic Enhance' },
              { id: 'bw', label: 'B&W Doc' },
              { id: 'color', label: 'Color' },
              { id: 'grayscale', label: 'Grayscale' }
            ].map((f) => (
              <button
                key={f.id}
                onClick={() => {
                  setActiveFilter(f.id as ScanFilter);
                  playHapticTick();
                }}
                className={`px-2.5 py-1 rounded-lg text-xs font-semibold transition-all cursor-pointer ${
                  activeFilter === f.id
                    ? 'bg-amber-500 text-stone-950 shadow-sm'
                    : 'text-stone-400 hover:text-stone-200 hover:bg-stone-800'
                }`}
              >
                {f.label}
              </button>
            ))}
          </div>
        </div>

        {/* Snapped Pages Strip */}
        {capturedPages.length > 0 && (
          <div className="px-4 py-2 bg-stone-950/60 border-t border-stone-800 flex items-center gap-2 overflow-x-auto">
            <span className="text-[11px] font-bold text-amber-400 whitespace-nowrap">
              {capturedPages.length} {capturedPages.length === 1 ? 'Page' : 'Pages'} Scanned:
            </span>
            <div className="flex items-center gap-1.5">
              {capturedPages.map((page, idx) => (
                <div
                  key={page.id}
                  className="relative w-11 h-11 rounded-lg overflow-hidden border border-stone-700 shrink-0"
                >
                  <img src={page.url} alt={`Scan ${idx + 1}`} className="w-full h-full object-cover" />
                  <div className="absolute top-0 left-0 w-3.5 h-3.5 bg-amber-500 text-stone-950 text-[8px] font-bold flex items-center justify-center rounded-br">
                    {idx + 1}
                  </div>
                  <button
                    onClick={() => setCapturedPages((prev) => prev.filter((p) => p.id !== page.id))}
                    className="absolute top-0 right-0 w-3.5 h-3.5 bg-rose-600 text-white text-[8px] font-bold flex items-center justify-center rounded-bl"
                  >
                    ✕
                  </button>
                </div>
              ))}
            </div>
          </div>
        )}

        {/* Bottom Shutter & Controls */}
        <div className="p-4 border-t border-stone-800 bg-stone-950/80 flex items-center justify-between">
          <button
            onClick={() => fileFallbackRef.current?.click()}
            className="px-3.5 py-2 rounded-xl text-stone-300 hover:text-white hover:bg-stone-800 text-xs font-semibold transition-colors flex items-center gap-1.5 cursor-pointer"
          >
            <Camera size={14} />
            <span>Upload Photo</span>
          </button>

          {/* Central Shutter Button */}
          <button
            onClick={handleCapturePhoto}
            className="w-14 h-14 rounded-full border-4 border-amber-500 flex items-center justify-center bg-stone-900 hover:scale-105 active:scale-95 transition-transform shadow-lg shadow-amber-500/20 cursor-pointer"
            title="Take Photo"
          >
            <div className="w-10 h-10 rounded-full bg-amber-500" />
          </button>

          {/* Finish & Add to Queue Button */}
          <button
            onClick={handleFinishScan}
            disabled={capturedPages.length === 0}
            className={`px-4 py-2 rounded-xl font-bold text-xs flex items-center gap-1.5 shadow-md transition-all cursor-pointer ${
              capturedPages.length > 0
                ? 'bg-emerald-500 hover:bg-emerald-400 text-stone-950 active:scale-95'
                : 'bg-stone-800 text-stone-500 cursor-not-allowed'
            }`}
          >
            <Check size={14} strokeWidth={2.5} />
            <span>Done ({capturedPages.length})</span>
          </button>
        </div>
      </div>
    </div>
  );
};
