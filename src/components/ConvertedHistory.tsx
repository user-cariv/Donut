import React, { useState } from 'react';
import {
  FolderCheck,
  Download,
  Trash2,
  Eye,
  FileText,
  Image,
  ExternalLink,
  HardDrive,
  Calendar,
  Layers,
  Share2
} from 'lucide-react';
import { ConvertedFile, FormatType } from '../types';
import { formatBytes, downloadFile, shareConvertedFile } from '../utils/converter';

interface ConvertedHistoryProps {
  files: ConvertedFile[];
  onDeleteFile: (id: string) => void;
  onClearAll: () => void;
}

export const ConvertedHistory: React.FC<ConvertedHistoryProps> = ({
  files,
  onDeleteFile,
  onClearAll
}) => {
  const [filter, setFilter] = useState<string>('all');
  const [previewFile, setPreviewFile] = useState<ConvertedFile | null>(null);

  const filteredFiles = files.filter((f) => {
    if (filter === 'all') return true;
    return f.format === filter;
  });

  const totalBytesSaved = files.reduce(
    (acc, f) => acc + Math.max(0, f.originalSizeBytes - f.convertedSizeBytes),
    0
  );

  return (
    <div className="flex flex-col h-full space-y-4">
      {/* Top Banner & Stats */}
      <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
        <div className="p-4 bg-stone-900/80 border border-stone-800 rounded-2xl flex items-center gap-3">
          <div className="w-10 h-10 rounded-xl bg-amber-500/10 border border-amber-500/30 flex items-center justify-center text-amber-500 shrink-0">
            <FolderCheck size={20} />
          </div>
          <div>
            <div className="text-[11px] text-stone-400">Total Converted</div>
            <div className="text-base font-bold text-stone-100 font-display">
              {files.length} {files.length === 1 ? 'file' : 'files'}
            </div>
          </div>
        </div>

        <div className="p-4 bg-stone-900/80 border border-stone-800 rounded-2xl flex items-center gap-3">
          <div className="w-10 h-10 rounded-xl bg-emerald-500/10 border border-emerald-500/30 flex items-center justify-center text-emerald-400 shrink-0">
            <HardDrive size={20} />
          </div>
          <div>
            <div className="text-[11px] text-stone-400">Storage Saved</div>
            <div className="text-base font-bold text-emerald-400 font-display">
              {formatBytes(totalBytesSaved)}
            </div>
          </div>
        </div>

        <div className="p-4 bg-stone-900/80 border border-stone-800 rounded-2xl flex items-center gap-3">
          <div className="w-10 h-10 rounded-xl bg-sky-500/10 border border-sky-500/30 flex items-center justify-center text-sky-400 shrink-0">
            <Layers size={20} />
          </div>
          <div>
            <div className="text-[11px] text-stone-400">Destination Folders</div>
            <div className="text-xs font-semibold text-stone-200 truncate">
              PDFs → Downloads / Images → Pictures
            </div>
          </div>
        </div>
      </div>

      {/* Toolbar */}
      <div className="flex items-center justify-between pb-2 border-b border-stone-800">
        <div className="flex items-center gap-1.5 p-1 bg-stone-950/60 rounded-xl border border-stone-800">
          {['all', 'pdf', 'jpg', 'png', 'webp'].map((fmt) => (
            <button
              key={fmt}
              onClick={() => setFilter(fmt)}
              className={`px-3 py-1 rounded-lg text-xs font-bold uppercase transition-all ${
                filter === fmt
                  ? 'bg-amber-500 text-stone-950'
                  : 'text-stone-400 hover:text-stone-200'
              }`}
            >
              {fmt}
            </button>
          ))}
        </div>

        {files.length > 0 && (
          <button
            onClick={onClearAll}
            className="px-3 py-1.5 rounded-lg text-xs font-semibold text-stone-400 hover:text-rose-400 hover:bg-rose-950/20 border border-stone-800 transition-colors"
          >
            Clear History
          </button>
        )}
      </div>

      {/* List */}
      <div className="flex-1 overflow-y-auto space-y-2">
        {filteredFiles.length === 0 ? (
          <div className="h-64 flex flex-col items-center justify-center text-center p-6 text-stone-500">
            <FolderCheck size={36} className="text-stone-700 mb-2" />
            <p className="text-xs font-semibold text-stone-400">No converted files yet</p>
            <p className="text-[11px] text-stone-500 mt-1">
              Select photos from the gallery and use the Donut radial wheel to export!
            </p>
          </div>
        ) : (
          filteredFiles.map((file) => (
            <div
              key={file.id}
              className="flex items-center justify-between p-3.5 bg-stone-900/60 border border-stone-800 rounded-2xl hover:border-stone-700 transition-colors"
            >
              <div className="flex items-center gap-3 min-w-0">
                <div
                  className={`w-10 h-10 rounded-xl flex items-center justify-center font-bold text-xs uppercase shrink-0 border ${
                    file.format === 'pdf'
                      ? 'bg-amber-500/10 text-amber-400 border-amber-500/30'
                      : file.format === 'jpg'
                      ? 'bg-emerald-500/10 text-emerald-400 border-emerald-500/30'
                      : file.format === 'png'
                      ? 'bg-violet-500/10 text-violet-400 border-violet-500/30'
                      : 'bg-sky-500/10 text-sky-400 border-sky-500/30'
                  }`}
                >
                  {file.format}
                </div>

                <div className="min-w-0">
                  <div className="text-xs font-bold text-stone-100 truncate">{file.filename}</div>
                  <div className="flex items-center gap-2 text-[11px] text-stone-400 mt-0.5">
                    <span className="font-mono text-stone-300">
                      {formatBytes(file.convertedSizeBytes)}
                    </span>
                    {file.originalSizeBytes > file.convertedSizeBytes && (
                      <span className="text-emerald-400 font-semibold">
                        (saved {formatBytes(file.originalSizeBytes - file.convertedSizeBytes)})
                      </span>
                    )}
                    <span>·</span>
                    <span>{new Date(file.timestamp).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}</span>
                  </div>
                </div>
              </div>

              <div className="flex items-center gap-1.5 shrink-0">
                <button
                  type="button"
                  onClick={() => shareConvertedFile(file)}
                  className="p-1.5 rounded-xl text-stone-400 hover:text-white hover:bg-stone-800 transition-colors cursor-pointer"
                  title="Share to WhatsApp / Android Apps"
                >
                  <Share2 size={15} />
                </button>

                <button
                  type="button"
                  onClick={() => downloadFile(file)}
                  className="px-3 py-1.5 rounded-xl bg-amber-500 hover:bg-amber-400 text-stone-950 text-xs font-bold transition-all shadow-sm flex items-center gap-1 cursor-pointer"
                  title="Download File"
                >
                  <Download size={13} strokeWidth={2.5} />
                  <span className="hidden sm:inline">Save</span>
                </button>

                <button
                  type="button"
                  onClick={() => onDeleteFile(file.id)}
                  className="p-2 rounded-xl text-stone-500 hover:text-rose-400 hover:bg-rose-950/30 transition-colors"
                  title="Remove from history"
                >
                  <Trash2 size={15} />
                </button>
              </div>
            </div>
          ))
        )}
      </div>
    </div>
  );
};
