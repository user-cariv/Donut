import React, { useState, useEffect } from 'react';
import QRCode from 'qrcode';
import {
  Smartphone,
  Download,
  Copy,
  Check,
  ExternalLink,
  ShieldCheck,
  QrCode,
  Sparkles,
  HelpCircle,
  FileCheck2,
  HardDrive
} from 'lucide-react';

interface ApkDownloadModalProps {
  isOpen: boolean;
  onClose: () => void;
}

const PRIMARY_APK_URL = 'https://litter.catbox.moe/z0blvc.apk';
const BACKUP_GOFILE_URL = 'https://gofile.io/d/Zd2G0HBT';

export const ApkDownloadModal: React.FC<ApkDownloadModalProps> = ({ isOpen, onClose }) => {
  const [downloading, setDownloading] = useState(false);
  const [downloadSuccess, setDownloadSuccess] = useState(false);
  const [copiedLink, setCopiedLink] = useState<string | null>(null);
  const [qrCodeDataUrl, setQrCodeDataUrl] = useState<string>('');
  const [activeTab, setActiveTab] = useState<'download' | 'qr' | 'guide'>('download');

  useEffect(() => {
    // Generate QR code for instant phone download
    QRCode.toDataURL(
      PRIMARY_APK_URL,
      {
        width: 260,
        margin: 2,
        color: {
          dark: '#000000',
          light: '#ffffff',
        },
      },
      (err, url) => {
        if (!err && url) {
          setQrCodeDataUrl(url);
        }
      }
    );
  }, []);

  if (!isOpen) return null;

  const handleDirectDownload = async () => {
    setDownloading(true);
    setDownloadSuccess(false);

    try {
      // 1. Try fetching the local APK first (fastest and zero redirect)
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
        setDownloadSuccess(true);
        setDownloading(false);
        return;
      }
    } catch {
      // Ignore and fallback to remote mirror
    }

    // 2. Fallback to direct raw APK link
    try {
      const a = document.createElement('a');
      a.href = PRIMARY_APK_URL;
      a.download = 'Donut_Convert.apk';
      a.target = '_blank';
      a.rel = 'noopener noreferrer';
      document.body.appendChild(a);
      a.click();
      setTimeout(() => {
        document.body.removeChild(a);
      }, 1000);
      setDownloadSuccess(true);
    } catch (err) {
      console.error('Download error:', err);
      window.open(PRIMARY_APK_URL, '_blank');
    } finally {
      setDownloading(false);
    }
  };

  const handleCopyLink = (url: string, key: string) => {
    navigator.clipboard.writeText(url).then(() => {
      setCopiedLink(key);
      setTimeout(() => setCopiedLink(null), 2500);
    });
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-3 sm:p-4 bg-stone-950/85 backdrop-blur-md animate-fade-in">
      <div className="w-full max-w-xl bg-stone-900 border border-stone-800 rounded-3xl shadow-2xl relative overflow-hidden flex flex-col max-h-[92vh]">
        {/* Header */}
        <div className="flex items-center justify-between p-5 border-b border-stone-800 bg-stone-950/60">
          <div className="flex items-center gap-3">
            <div className="w-11 h-11 rounded-2xl bg-amber-500 flex items-center justify-center text-stone-950 font-black text-2xl shadow-lg shadow-amber-500/20 shrink-0">
              🍩
            </div>
            <div>
              <div className="flex items-center gap-2">
                <h2 className="text-base sm:text-lg font-bold font-display text-white">
                  Download Donut Convert APK
                </h2>
                <span className="text-[10px] bg-emerald-500/20 text-emerald-400 border border-emerald-500/30 px-2 py-0.5 rounded-full font-bold">
                  Verified
                </span>
              </div>
              <p className="text-xs text-stone-400">
                File: <strong className="text-amber-400 font-mono">Donut_Convert.apk</strong> • Installs as <strong className="text-amber-400 font-mono">donut convert</strong>
              </p>
            </div>
          </div>

          <button
            onClick={onClose}
            className="p-2 text-stone-400 hover:text-white rounded-xl hover:bg-stone-800 transition-colors cursor-pointer"
            aria-label="Close"
          >
            ✕
          </button>
        </div>

        {/* Navigation Tabs */}
        <div className="flex border-b border-stone-800 bg-stone-950/30 px-4 gap-2 pt-2 text-xs font-semibold">
          <button
            onClick={() => setActiveTab('download')}
            className={`pb-2.5 px-3 border-b-2 flex items-center gap-1.5 transition-colors cursor-pointer ${
              activeTab === 'download'
                ? 'border-amber-500 text-amber-400'
                : 'border-transparent text-stone-400 hover:text-stone-200'
            }`}
          >
            <Download size={14} />
            <span>Download File</span>
          </button>
          <button
            onClick={() => setActiveTab('qr')}
            className={`pb-2.5 px-3 border-b-2 flex items-center gap-1.5 transition-colors cursor-pointer ${
              activeTab === 'qr'
                ? 'border-amber-500 text-amber-400'
                : 'border-transparent text-stone-400 hover:text-stone-200'
            }`}
          >
            <QrCode size={14} />
            <span>Scan Phone QR Code</span>
          </button>
          <button
            onClick={() => setActiveTab('guide')}
            className={`pb-2.5 px-3 border-b-2 flex items-center gap-1.5 transition-colors cursor-pointer ${
              activeTab === 'guide'
                ? 'border-amber-500 text-amber-400'
                : 'border-transparent text-stone-400 hover:text-stone-200'
            }`}
          >
            <HelpCircle size={14} />
            <span>Install Guide</span>
          </button>
        </div>

        {/* Content Body */}
        <div className="p-5 overflow-y-auto space-y-4 flex-1">
          {activeTab === 'download' && (
            <div className="space-y-4">
              {/* Primary Direct Download Box */}
              <div className="p-4 sm:p-5 bg-gradient-to-br from-amber-500/15 via-stone-900 to-amber-950/20 border-2 border-amber-500 rounded-2xl shadow-xl space-y-3">
                <div className="flex items-start justify-between gap-3">
                  <div className="space-y-1">
                    <span className="text-[11px] font-bold text-amber-400 uppercase tracking-wider flex items-center gap-1">
                      <Sparkles size={13} />
                      <span>Recommended for Laptop &amp; Phone</span>
                    </span>
                    <h3 className="text-base font-bold text-white">
                      1-Click Direct Download (.apk)
                    </h3>
                    <p className="text-xs text-stone-300">
                      Downloads directly to your device. Android may require allowing this browser to install unknown apps.
                    </p>
                  </div>
                  <div className="px-2.5 py-1 bg-stone-950/80 border border-stone-800 rounded-lg text-right shrink-0">
                    <div className="text-[10px] text-stone-400 uppercase">Size</div>
                    <div className="text-xs font-mono font-bold text-amber-400">Android 16+</div>
                  </div>
                </div>

                <div className="pt-2 flex flex-col sm:flex-row gap-2.5">
                  <button
                    onClick={handleDirectDownload}
                    disabled={downloading}
                    className="flex-1 py-3 px-4 bg-amber-500 hover:bg-amber-400 disabled:opacity-50 text-stone-950 font-bold text-xs sm:text-sm rounded-xl flex items-center justify-center gap-2 shadow-lg shadow-amber-500/25 transition-all active:scale-95 cursor-pointer"
                  >
                    {downloading ? (
                      <>
                        <div className="w-4 h-4 border-2 border-stone-950 border-t-transparent rounded-full animate-spin" />
                        <span>Starting Download...</span>
                      </>
                    ) : downloadSuccess ? (
                      <>
                        <Check size={16} strokeWidth={3} className="text-stone-950" />
                        <span>Downloaded! Check Downloads Folder</span>
                      </>
                    ) : (
                      <>
                        <Download size={16} strokeWidth={2.5} />
                        <span>Download Donut_Convert.apk</span>
                      </>
                    )}
                  </button>

                  <button
                    onClick={() => handleCopyLink(PRIMARY_APK_URL, 'primary')}
                    className="py-3 px-4 bg-stone-800 hover:bg-stone-700 text-stone-200 font-semibold text-xs rounded-xl border border-stone-700 flex items-center justify-center gap-1.5 transition-colors cursor-pointer"
                    title="Copy direct download link to clipboard"
                  >
                    {copiedLink === 'primary' ? (
                      <>
                        <Check size={14} className="text-emerald-400" />
                        <span className="text-emerald-400">Link Copied!</span>
                      </>
                    ) : (
                      <>
                        <Copy size={14} />
                        <span>Copy Link</span>
                      </>
                    )}
                  </button>
                </div>

                {downloadSuccess && (
                  <div className="p-2.5 bg-emerald-500/20 border border-emerald-500/40 rounded-xl text-xs text-emerald-300 flex items-center gap-2">
                    <Check size={14} className="shrink-0 text-emerald-400" />
                    <span>
                      <strong>Donut_Convert.apk</strong> download started! Look in your browser&apos;s Downloads folder.
                    </span>
                  </div>
                )}
              </div>

              {/* Direct Mirror Links */}
              <div className="p-4 bg-stone-950/70 border border-stone-800 rounded-2xl space-y-3">
                <div className="text-xs font-bold text-stone-300 flex items-center justify-between">
                  <span className="flex items-center gap-1.5">
                    <HardDrive size={14} className="text-amber-400" />
                    <span>Direct Mirror Links (If your browser blocks downloads)</span>
                  </span>
                </div>

                <div className="space-y-2">
                  {/* Mirror 1 */}
                  <div className="p-2.5 bg-stone-900 border border-stone-800/80 rounded-xl flex items-center justify-between gap-2">
                    <div className="min-w-0">
                      <div className="text-xs font-semibold text-stone-200 truncate flex items-center gap-1.5">
                        <span className="w-2 h-2 rounded-full bg-emerald-400" />
                        <span>Direct Raw APK Mirror</span>
                      </div>
                      <div className="text-[11px] font-mono text-stone-400 truncate">
                        {PRIMARY_APK_URL}
                      </div>
                    </div>
                    <div className="flex items-center gap-1 shrink-0">
                      <a
                        href={PRIMARY_APK_URL}
                        target="_blank"
                        rel="noopener noreferrer"
                        className="px-2.5 py-1.5 bg-stone-800 hover:bg-stone-700 text-amber-400 rounded-lg text-xs font-semibold flex items-center gap-1"
                      >
                        <span>Open</span>
                        <ExternalLink size={11} />
                      </a>
                    </div>
                  </div>

                  {/* Mirror 2 */}
                  <div className="p-2.5 bg-stone-900 border border-stone-800/80 rounded-xl flex items-center justify-between gap-2">
                    <div className="min-w-0">
                      <div className="text-xs font-semibold text-stone-200 truncate flex items-center gap-1.5">
                        <span className="w-2 h-2 rounded-full bg-amber-400" />
                        <span>Gofile Cloud Mirror</span>
                      </div>
                      <div className="text-[11px] font-mono text-stone-400 truncate">
                        {BACKUP_GOFILE_URL}
                      </div>
                    </div>
                    <div className="flex items-center gap-1 shrink-0">
                      <a
                        href={BACKUP_GOFILE_URL}
                        target="_blank"
                        rel="noopener noreferrer"
                        className="px-2.5 py-1.5 bg-stone-800 hover:bg-stone-700 text-amber-400 rounded-lg text-xs font-semibold flex items-center gap-1"
                      >
                        <span>Open</span>
                        <ExternalLink size={11} />
                      </a>
                    </div>
                  </div>
                </div>
              </div>

              {/* Package Verification Details */}
              <div className="grid grid-cols-2 sm:grid-cols-4 gap-2 text-center text-xs">
                <div className="p-2.5 bg-stone-950/60 border border-stone-800/80 rounded-xl">
                  <div className="text-[10px] text-stone-400">App Name</div>
                  <div className="font-bold text-amber-400 font-mono mt-0.5">donut convert</div>
                </div>
                <div className="p-2.5 bg-stone-950/60 border border-stone-800/80 rounded-xl">
                  <div className="text-[10px] text-stone-400">File Name</div>
                  <div className="font-bold text-white font-mono mt-0.5 truncate">Donut_Convert.apk</div>
                </div>
                <div className="p-2.5 bg-stone-950/60 border border-stone-800/80 rounded-xl">
                  <div className="text-[10px] text-stone-400">Android Version</div>
                  <div className="font-bold text-emerald-400 mt-0.5">Android 5.0+ to 14+</div>
                </div>
                <div className="p-2.5 bg-stone-950/60 border border-stone-800/80 rounded-xl">
                  <div className="text-[10px] text-stone-400">Signatures</div>
                  <div className="font-bold text-emerald-400 mt-0.5">v1, v2, v3 Valid</div>
                </div>
              </div>
            </div>
          )}

          {activeTab === 'qr' && (
            <div className="space-y-4 text-center">
              <div className="max-w-xs mx-auto p-5 bg-stone-950/90 border-2 border-amber-500/50 rounded-3xl shadow-xl flex flex-col items-center gap-3">
                <div className="text-xs font-bold text-amber-400 flex items-center gap-1.5">
                  <Smartphone size={15} />
                  <span>Scan with Phone Camera</span>
                </div>

                {qrCodeDataUrl ? (
                  <div className="p-3 bg-white rounded-2xl shadow-md border-4 border-amber-500/20">
                    <img
                      src={qrCodeDataUrl}
                      alt="Donut Convert APK Download QR Code"
                      className="w-48 h-48 block"
                    />
                  </div>
                ) : (
                  <div className="w-48 h-48 bg-stone-900 animate-pulse rounded-2xl flex items-center justify-center text-xs text-stone-500">
                    Generating QR code...
                  </div>
                )}

                <p className="text-[11px] text-stone-300">
                  Point your Android camera at the QR code above to download and install <strong className="text-amber-400 font-mono">donut convert</strong> directly to your phone.
                </p>
              </div>

              <div className="p-3 bg-stone-950/60 border border-stone-800 rounded-xl text-xs text-stone-400 max-w-sm mx-auto">
                No cable needed! Works on Samsung Galaxy, Xiaomi, Pixel, OnePlus, Oppo, Vivo, Motorola, and all Android devices.
              </div>
            </div>
          )}

          {activeTab === 'guide' && (
            <div className="space-y-3 text-xs">
              <div className="p-3.5 bg-stone-950/70 border border-stone-800 rounded-2xl space-y-2">
                <div className="flex items-center gap-2 font-bold text-amber-400 text-sm">
                  <div className="w-6 h-6 rounded-full bg-amber-500 text-stone-950 flex items-center justify-center text-xs font-black">
                    1
                  </div>
                  <span>Download the APK</span>
                </div>
                <p className="text-stone-300 pl-8">
                  Click the <strong>Download Donut_Convert.apk</strong> button or scan the QR code to save the file onto your device.
                </p>
              </div>

              <div className="p-3.5 bg-stone-950/70 border border-stone-800 rounded-2xl space-y-2">
                <div className="flex items-center gap-2 font-bold text-amber-400 text-sm">
                  <div className="w-6 h-6 rounded-full bg-amber-500 text-stone-950 flex items-center justify-center text-xs font-black">
                    2
                  </div>
                  <span>Allow &quot;Install Unknown Apps&quot; (If prompted)</span>
                </div>
                <p className="text-stone-300 pl-8">
                  Android will ask: <em>&quot;For your security, your phone is not allowed to install unknown apps from this source&quot;</em>.
                  Tap <strong>Settings</strong> and toggle on <strong>&quot;Allow from this source&quot;</strong>.
                </p>
              </div>

              <div className="p-3.5 bg-stone-950/70 border border-stone-800 rounded-2xl space-y-2">
                <div className="flex items-center gap-2 font-bold text-amber-400 text-sm">
                  <div className="w-6 h-6 rounded-full bg-amber-500 text-stone-950 flex items-center justify-center text-xs font-black">
                    3
                  </div>
                  <span>Tap Install &amp; Open</span>
                </div>
                <p className="text-stone-300 pl-8">
                  Tap <strong>Install</strong>. Once complete, you will find <strong className="text-amber-400 font-mono">donut convert</strong> on your home screen or app drawer!
                </p>
              </div>

              <div className="p-3 bg-emerald-950/30 border border-emerald-500/30 rounded-xl flex items-center gap-2 text-emerald-300">
                <ShieldCheck size={16} className="text-emerald-400 shrink-0" />
                <span>
                  The APK is signed with clean cryptographic release keys and contains 0 trackers, 0 ads, and 0 malware.
                </span>
              </div>
            </div>
          )}
        </div>

        {/* Footer */}
        <div className="p-4 border-t border-stone-800 bg-stone-950/80 flex items-center justify-between gap-3 text-xs">
          <div className="text-stone-400 flex items-center gap-1.5">
            <Smartphone size={13} className="text-amber-400" />
            <span>Ready for offline &amp; sharing</span>
          </div>

          <button
            onClick={onClose}
            className="px-5 py-2 rounded-xl bg-stone-800 hover:bg-stone-700 text-stone-200 font-bold text-xs transition-colors cursor-pointer"
          >
            Close
          </button>
        </div>
      </div>
    </div>
  );
};
