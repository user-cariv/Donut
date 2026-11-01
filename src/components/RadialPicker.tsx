import React, { useState, useRef, useCallback } from 'react';
import { motion, AnimatePresence } from 'motion/react';
import { FileText, Image, Sparkles, Zap, Check } from 'lucide-react';
import { FormatType } from '../types';
import { playHapticTick, playSectorSelect } from '../utils/audio';

interface RadialPickerProps {
  selectedCount: number;
  onSelectFormat: (format: FormatType) => void;
  disabled?: boolean;
  activeFormat?: FormatType;
}

interface SectorConfig {
  id: FormatType;
  label: string;
  sub: string;
  color: string;
  activeBg: string;
  textColor: string;
  accentHex: string;
  startAngle: number; // degrees
  endAngle: number;
  icon: React.ComponentType<{ className?: string; size?: number }>;
}

const SECTORS: SectorConfig[] = [
  {
    id: 'pdf',
    label: 'PDF',
    sub: 'Doc / Pages',
    color: 'from-amber-500 to-orange-600',
    activeBg: 'bg-amber-500',
    textColor: 'text-amber-400',
    accentHex: '#f59e0b',
    startAngle: -45,
    endAngle: 45,
    icon: FileText
  },
  {
    id: 'jpg',
    label: 'JPG',
    sub: 'Photo Standard',
    color: 'from-emerald-500 to-teal-600',
    activeBg: 'bg-emerald-500',
    textColor: 'text-emerald-400',
    accentHex: '#10b981',
    startAngle: 45,
    endAngle: 135,
    icon: Image
  },
  {
    id: 'png',
    label: 'PNG',
    sub: 'Lossless / Crisp',
    color: 'from-violet-500 to-purple-600',
    activeBg: 'bg-violet-500',
    textColor: 'text-violet-400',
    accentHex: '#8b5cf6',
    startAngle: 135,
    endAngle: 225,
    icon: Sparkles
  },
  {
    id: 'webp',
    label: 'WEBP',
    sub: 'Ultra Compact',
    color: 'from-sky-500 to-blue-600',
    activeBg: 'bg-sky-500',
    textColor: 'text-sky-400',
    accentHex: '#0ea5e9',
    startAngle: 225,
    endAngle: 315,
    icon: Zap
  }
];

export const RadialPicker: React.FC<RadialPickerProps> = ({
  selectedCount,
  onSelectFormat,
  disabled = false,
  activeFormat = 'pdf'
}) => {
  const [hoveredSector, setHoveredSector] = useState<FormatType | null>(null);
  const [isInteracting, setIsInteracting] = useState(false);
  const [activeDragSector, setActiveDragSector] = useState<FormatType | null>(null);
  const donutRef = useRef<HTMLDivElement>(null);

  const getAngleFromEvent = useCallback((clientX: number, clientY: number): number | null => {
    if (!donutRef.current) return null;
    const rect = donutRef.current.getBoundingClientRect();
    const centerX = rect.left + rect.width / 2;
    const centerY = rect.top + rect.height / 2;
    const dx = clientX - centerX;
    const dy = clientY - centerY;
    
    // Distance check: ignore if inside inner hole or outside outer boundary
    const dist = Math.sqrt(dx * dx + dy * dy);
    if (dist < 40 || dist > 180) return null;

    let deg = (Math.atan2(dy, dx) * 180) / Math.PI; // -180 to 180
    if (deg < -45) deg += 360;
    return deg;
  }, []);

  const getSectorFromAngle = useCallback((deg: number): FormatType => {
    for (const sec of SECTORS) {
      if (sec.startAngle === -45 && sec.endAngle === 45) {
        if (deg >= -45 && deg < 45) return sec.id;
      } else if (deg >= sec.startAngle && deg < sec.endAngle) {
        return sec.id;
      }
    }
    return 'pdf';
  }, []);

  const handlePointerDown = (e: React.PointerEvent) => {
    if (disabled) return;
    setIsInteracting(true);
    const angle = getAngleFromEvent(e.clientX, e.clientY);
    if (angle !== null) {
      const sec = getSectorFromAngle(angle);
      setActiveDragSector(sec);
      playHapticTick();
    }
  };

  const handlePointerMove = (e: React.PointerEvent) => {
    if (!isInteracting) return;
    const angle = getAngleFromEvent(e.clientX, e.clientY);
    if (angle !== null) {
      const sec = getSectorFromAngle(angle);
      if (sec !== activeDragSector) {
        setActiveDragSector(sec);
        playHapticTick();
      }
    }
  };

  const handlePointerUp = () => {
    if (isInteracting && activeDragSector) {
      playSectorSelect();
      onSelectFormat(activeDragSector);
    }
    setIsInteracting(false);
    setActiveDragSector(null);
  };

  const currentDisplayFormat = activeDragSector || hoveredSector || activeFormat;
  const currentSectorObj = SECTORS.find((s) => s.id === currentDisplayFormat) || SECTORS[0];

  // SVG arc calculation
  // Center: 150, 150. Outer R: 135, Inner R: 62.
  const createDonutArc = (startDeg: number, endDeg: number, innerR = 62, outerR = 135) => {
    // Small gap between sectors (2.5 deg)
    const gap = 3;
    const sRad = ((startDeg + gap) * Math.PI) / 180;
    const eRad = ((endDeg - gap) * Math.PI) / 180;

    const x1 = 150 + outerR * Math.cos(sRad);
    const y1 = 150 + outerR * Math.sin(sRad);
    const x2 = 150 + outerR * Math.cos(eRad);
    const y2 = 150 + outerR * Math.sin(eRad);

    const x3 = 150 + innerR * Math.cos(eRad);
    const y3 = 150 + innerR * Math.sin(eRad);
    const x4 = 150 + innerR * Math.cos(sRad);
    const y4 = 150 + innerR * Math.sin(sRad);

    return `M ${x1} ${y1} A ${outerR} ${outerR} 0 0 1 ${x2} ${y2} L ${x3} ${y3} A ${innerR} ${innerR} 0 0 0 ${x4} ${y4} Z`;
  };

  return (
    <div className="flex flex-col items-center select-none">
      <div
        ref={donutRef}
        onPointerDown={handlePointerDown}
        onPointerMove={handlePointerMove}
        onPointerUp={handlePointerUp}
        onPointerCancel={() => {
          setIsInteracting(false);
          setActiveDragSector(null);
        }}
        className={`relative w-72 h-72 sm:w-80 sm:h-80 flex items-center justify-center touch-none transition-opacity cursor-pointer ${
          disabled ? 'opacity-50 cursor-not-allowed' : ''
        }`}
      >
        {/* Glow halo behind donut */}
        <div
          className="absolute inset-4 rounded-full blur-2xl transition-all duration-300 pointer-events-none"
          style={{
            background: `radial-gradient(circle, ${currentSectorObj.accentHex}33 0%, transparent 70%)`
          }}
        />

        {/* SVG Donut Ring */}
        <svg
          viewBox="0 0 300 300"
          className="w-full h-full drop-shadow-xl transform-gpu"
        >
          {/* Subtle Outer Track ring */}
          <circle cx="150" cy="150" r="138" fill="none" stroke="rgba(255,255,255,0.06)" strokeWidth="2" />
          <circle cx="150" cy="150" r="59" fill="none" stroke="rgba(255,255,255,0.08)" strokeWidth="2" />

          {/* 4 Donut Sectors */}
          {SECTORS.map((sector) => {
            const isHovered = hoveredSector === sector.id;
            const isSectorActive = currentDisplayFormat === sector.id;
            const pathData = createDonutArc(sector.startAngle, sector.endAngle);

            // Icon position center along the arc
            const midDeg = (sector.startAngle + sector.endAngle) / 2;
            const midRad = (midDeg * Math.PI) / 180;
            const iconR = 98;
            const iconX = 150 + iconR * Math.cos(midRad);
            const iconY = 150 + iconR * Math.sin(midRad);

            return (
              <g
                key={sector.id}
                onMouseEnter={() => {
                  setHoveredSector(sector.id);
                  playHapticTick();
                }}
                onMouseLeave={() => setHoveredSector(null)}
                onClick={() => {
                  if (disabled) return;
                  playSectorSelect();
                  onSelectFormat(sector.id);
                }}
                className="transition-transform duration-200 cursor-pointer"
              >
                {/* Sector wedge */}
                <path
                  d={pathData}
                  fill={isSectorActive ? sector.accentHex : '#262422'}
                  opacity={isSectorActive ? 0.95 : 0.8}
                  stroke={isSectorActive ? '#ffffff' : 'rgba(255,255,255,0.12)'}
                  strokeWidth={isSectorActive ? 2.5 : 1}
                  className="transition-all duration-200 hover:opacity-100"
                />

                {/* Donut sprinkles decorative overlay on active sector */}
                {isSectorActive && (
                  <g opacity="0.6">
                    <line
                      x1={iconX - 22}
                      y1={iconY - 14}
                      x2={iconX - 14}
                      y2={iconY - 10}
                      stroke="#ffffff"
                      strokeWidth="3"
                      strokeLinecap="round"
                    />
                    <line
                      x1={iconX + 16}
                      y1={iconY + 12}
                      x2={iconX + 22}
                      y2={iconY + 6}
                      stroke="#ffffff"
                      strokeWidth="3"
                      strokeLinecap="round"
                    />
                  </g>
                )}

                {/* Sector Text & Icon */}
                <text
                  x={iconX}
                  y={iconY - 4}
                  textAnchor="middle"
                  dominantBaseline="central"
                  fill={isSectorActive ? '#ffffff' : '#e7e5e4'}
                  fontSize="17"
                  fontFamily="'Outfit', sans-serif"
                  fontWeight="700"
                  letterSpacing="0.05em"
                  pointerEvents="none"
                >
                  {sector.label}
                </text>

                <text
                  x={iconX}
                  y={iconY + 16}
                  textAnchor="middle"
                  dominantBaseline="central"
                  fill={isSectorActive ? 'rgba(255,255,255,0.9)' : '#a8a29e'}
                  fontSize="10"
                  fontFamily="sans-serif"
                  fontWeight="500"
                  pointerEvents="none"
                >
                  {sector.id.toUpperCase()}
                </text>
              </g>
            );
          })}
        </svg>

        {/* Center Donut Hole Action Hub */}
        <div className="absolute inset-0 flex items-center justify-center pointer-events-none">
          <motion.button
            type="button"
            whileHover={{ scale: 1.05 }}
            whileTap={{ scale: 0.95 }}
            onClick={() => {
              if (!disabled) {
                playSectorSelect();
                onSelectFormat(currentDisplayFormat);
              }
            }}
            disabled={disabled}
            className="w-28 h-28 sm:w-32 sm:h-32 rounded-full pointer-events-auto flex flex-col items-center justify-center p-3 text-center transition-all duration-200 border shadow-2xl bg-stone-900/95 border-amber-500/50 text-stone-100 hover:border-amber-400 active:border-amber-500 hover:shadow-amber-500/10"
          >
            <div className="flex items-center gap-1 text-[11px] font-bold uppercase tracking-wider text-amber-400">
              <Sparkles size={13} className="text-amber-400 animate-pulse" />
              <span>Convert</span>
            </div>

            <div className="font-display font-extrabold text-xl text-white my-0.5 tracking-tight">
              {currentSectorObj.label}
            </div>

            <div className="text-[11px] text-stone-300 font-medium tabular-nums">
              {selectedCount > 0 ? (
                <span className="text-amber-300 font-semibold">{selectedCount} {selectedCount === 1 ? 'item' : 'items'}</span>
              ) : (
                <span className="text-stone-400">Convert Now</span>
              )}
            </div>
          </motion.button>
        </div>
      </div>

      {/* Sector Quick Buttons Bar */}
      <div className="mt-4 flex items-center gap-1.5 p-1.5 bg-stone-950/70 border border-stone-800 rounded-full backdrop-blur-md">
        {SECTORS.map((sec) => {
          const isSelected = activeFormat === sec.id;
          return (
            <button
              key={sec.id}
              onClick={() => {
                if (disabled) return;
                playSectorSelect();
                onSelectFormat(sec.id);
              }}
              disabled={disabled}
              className={`px-3 py-1.5 rounded-full text-xs font-bold transition-all flex items-center gap-1.5 cursor-pointer ${
                isSelected
                  ? `${sec.activeBg} text-stone-950 shadow-md`
                  : 'text-stone-400 hover:text-stone-200 hover:bg-stone-800/60'
              }`}
            >
              <sec.icon size={13} />
              <span>{sec.label}</span>
              {isSelected && <Check size={12} strokeWidth={3} />}
            </button>
          );
        })}
      </div>

      <p className="text-[12px] text-stone-400 mt-2 text-center max-w-xs">
        Tap or slide around the donut ring to set output format
      </p>
    </div>
  );
};
