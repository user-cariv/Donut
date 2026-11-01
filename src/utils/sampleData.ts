import { GalleryItem } from '../types';

function createSvgDataUrl(svgString: string): string {
  return `data:image/svg+xml;charset=utf-8,${encodeURIComponent(svgString.trim())}`;
}

export const SAMPLE_GALLERY_ITEMS: GalleryItem[] = [
  {
    id: 'sample-doc-1',
    name: 'Invoice_TechCorp_Oct2026.png',
    type: 'image/png',
    sizeBytes: 342000,
    width: 1200,
    height: 1600,
    dateAdded: 'Today, 08:30 AM',
    category: 'document',
    url: createSvgDataUrl(`
      <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1200 1600" width="1200" height="1600">
        <rect width="1200" height="1600" fill="#faf9f6"/>
        <rect x="80" y="80" width="1040" height="1440" fill="#ffffff" rx="8" filter="drop-shadow(0 4px 12px rgba(0,0,0,0.06))"/>
        
        <!-- Header -->
        <rect x="140" y="140" width="120" height="40" rx="6" fill="#f97316"/>
        <text x="160" y="166" font-family="sans-serif" font-weight="bold" font-size="20" fill="#ffffff">DONUT</text>
        <text x="800" y="170" font-family="sans-serif" font-weight="800" font-size="36" fill="#1e293b">INVOICE</text>
        <text x="800" y="200" font-family="sans-serif" font-size="16" fill="#64748b">INV-2026-0894 · Due Nov 15</text>

        <!-- Client & Biller -->
        <line x1="140" y1="230" x2="1060" y2="230" stroke="#e2e8f0" stroke-width="2"/>
        <text x="140" y="270" font-family="sans-serif" font-size="14" font-weight="bold" fill="#94a3b8">BILLED TO:</text>
        <text x="140" y="300" font-family="sans-serif" font-size="18" font-weight="600" fill="#0f172a">Ariv Choudhary</text>
        <text x="140" y="325" font-family="sans-serif" font-size="15" fill="#475569">440 Tech Square Way, Suite 700</text>
        <text x="140" y="350" font-family="sans-serif" font-size="15" fill="#475569">San Francisco, CA 94107</text>

        <text x="700" y="270" font-family="sans-serif" font-size="14" font-weight="bold" fill="#94a3b8">ISSUED BY:</text>
        <text x="700" y="300" font-family="sans-serif" font-size="18" font-weight="600" fill="#0f172a">Donut Convert Labs</text>
        <text x="700" y="325" font-family="sans-serif" font-size="15" fill="#475569">support@donutconvert.app</text>

        <!-- Table Header -->
        <rect x="140" y="400" width="920" height="48" rx="6" fill="#f1f5f9"/>
        <text x="160" y="430" font-family="sans-serif" font-weight="bold" font-size="14" fill="#334155">DESCRIPTION</text>
        <text x="640" y="430" font-family="sans-serif" font-weight="bold" font-size="14" fill="#334155">QTY</text>
        <text x="760" y="430" font-family="sans-serif" font-weight="bold" font-size="14" fill="#334155">RATE</text>
        <text x="960" y="430" font-family="sans-serif" font-weight="bold" font-size="14" fill="#334155" text-anchor="end">AMOUNT</text>

        <!-- Row 1 -->
        <text x="160" y="490" font-family="sans-serif" font-size="16" font-weight="500" fill="#1e293b">Jetpack Compose UI Engineering</text>
        <text x="160" y="515" font-family="sans-serif" font-size="13" fill="#64748b">Radial Donut Picker, Multi-page PDF Engine</text>
        <text x="640" y="495" font-family="sans-serif" font-size="15" fill="#334155">40 hrs</text>
        <text x="760" y="495" font-family="sans-serif" font-size="15" fill="#334155">$85.00</text>
        <text x="960" y="495" font-family="sans-serif" font-size="16" font-weight="600" fill="#0f172a" text-anchor="end">$3,400.00</text>

        <!-- Row 2 -->
        <line x1="140" y1="540" x2="1060" y2="540" stroke="#f1f5f9" stroke-width="1.5"/>
        <text x="160" y="580" font-family="sans-serif" font-size="16" font-weight="500" fill="#1e293b">Batch Stream Saver Optimization</text>
        <text x="160" y="605" font-family="sans-serif" font-size="13" fill="#64748b">Zero memory leak streaming bitmap builder</text>
        <text x="640" y="585" font-family="sans-serif" font-size="15" fill="#334155">12 hrs</text>
        <text x="760" y="585" font-family="sans-serif" font-size="15" fill="#334155">$95.00</text>
        <text x="960" y="585" font-family="sans-serif" font-size="16" font-weight="600" fill="#0f172a" text-anchor="end">$1,140.00</text>

        <!-- Total Box -->
        <rect x="700" y="700" width="360" height="150" rx="8" fill="#f8fafc" stroke="#e2e8f0"/>
        <text x="720" y="740" font-family="sans-serif" font-size="15" fill="#64748b">Subtotal:</text>
        <text x="1040" y="740" font-family="sans-serif" font-size="15" fill="#1e293b" text-anchor="end">$4,540.00</text>
        <text x="720" y="775" font-family="sans-serif" font-size="15" fill="#64748b">Taxes (0%):</text>
        <text x="1040" y="775" font-family="sans-serif" font-size="15" fill="#1e293b" text-anchor="end">$0.00</text>
        <line x1="720" y1="795" x2="1040" y2="795" stroke="#cbd5e1"/>
        <text x="720" y="830" font-family="sans-serif" font-size="18" font-weight="bold" fill="#0f172a">TOTAL DUE:</text>
        <text x="1040" y="830" font-family="sans-serif" font-size="20" font-weight="bold" fill="#ea580c" text-anchor="end">$4,540.00</text>

        <!-- Barcode Stamp -->
        <rect x="140" y="1360" width="220" height="60" fill="#1e293b" rx="4"/>
        <text x="250" y="1395" font-family="monospace" font-size="18" fill="#ffffff" text-anchor="middle">|||| | | |||| |</text>
        <text x="800" y="1400" font-family="sans-serif" font-size="14" fill="#94a3b8">Thank you for your business · Donut Convert</text>
      </svg>
    `)
  },
  {
    id: 'sample-doc-2',
    name: 'Receipt_Bakery_Artisan.jpg',
    type: 'image/jpeg',
    sizeBytes: 215000,
    width: 800,
    height: 1400,
    dateAdded: 'Today, 09:12 AM',
    category: 'receipt',
    url: createSvgDataUrl(`
      <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 800 1400" width="800" height="1400">
        <rect width="800" height="1400" fill="#f5ebe0"/>
        <!-- Thermal paper receipt -->
        <rect x="80" y="60" width="640" height="1280" fill="#ffffff" filter="drop-shadow(0 6px 16px rgba(0,0,0,0.08))"/>
        
        <text x="400" y="130" font-family="monospace" font-size="26" font-weight="bold" fill="#292524" text-anchor="middle">★ SWEET DONUT CAFE ★</text>
        <text x="400" y="165" font-family="monospace" font-size="15" fill="#78716c" text-anchor="middle">88 Cinnamon Boulevard, Suite 3</text>
        <text x="400" y="190" font-family="monospace" font-size="14" fill="#78716c" text-anchor="middle">Tel: (555) 366-8822</text>
        <text x="400" y="225" font-family="monospace" font-size="14" fill="#57534e" text-anchor="middle">2026-10-31 · 08:44:12 AM · TBL 04</text>
        <line x1="120" y1="245" x2="680" y2="245" stroke="#a8a29e" stroke-dasharray="6,4" stroke-width="1.5"/>

        <!-- Items -->
        <text x="120" y="285" font-family="monospace" font-size="16" fill="#292524">1x Glazed Donut Box (Dozen)</text>
        <text x="680" y="285" font-family="monospace" font-size="16" fill="#292524" text-anchor="end">$18.50</text>
        
        <text x="120" y="325" font-family="monospace" font-size="16" fill="#292524">2x Oat Milk Cappuccino</text>
        <text x="680" y="325" font-family="monospace" font-size="16" fill="#292524" text-anchor="end">$11.00</text>

        <text x="120" y="365" font-family="monospace" font-size="16" fill="#292524">1x Maple Bacon Twist</text>
        <text x="680" y="365" font-family="monospace" font-size="16" fill="#292524" text-anchor="end">$4.75</text>

        <text x="120" y="405" font-family="monospace" font-size="16" fill="#292524">1x Cold Brew Nitro 16oz</text>
        <text x="680" y="405" font-family="monospace" font-size="16" fill="#292524" text-anchor="end">$5.25</text>

        <line x1="120" y1="435" x2="680" y2="435" stroke="#a8a29e" stroke-dasharray="6,4" stroke-width="1.5"/>

        <text x="120" y="475" font-family="monospace" font-size="16" fill="#78716c">SUBTOTAL</text>
        <text x="680" y="475" font-family="monospace" font-size="16" fill="#292524" text-anchor="end">$39.50</text>
        <text x="120" y="510" font-family="monospace" font-size="16" fill="#78716c">SALES TAX (8.25%)</text>
        <text x="680" y="510" font-family="monospace" font-size="16" fill="#292524" text-anchor="end">$3.26</text>
        <text x="120" y="545" font-family="monospace" font-size="16" fill="#78716c">TIP (18%)</text>
        <text x="680" y="545" font-family="monospace" font-size="16" fill="#292524" text-anchor="end">$7.11</text>

        <line x1="120" y1="575" x2="680" y2="575" stroke="#292524" stroke-width="2"/>
        <text x="120" y="615" font-family="monospace" font-size="22" font-weight="bold" fill="#292524">TOTAL</text>
        <text x="680" y="615" font-family="monospace" font-size="24" font-weight="bold" fill="#ea580c" text-anchor="end">$49.87</text>

        <text x="400" y="700" font-family="monospace" font-size="14" fill="#a8a29e" text-anchor="middle">CARD: VISA ···· 9042 APPROVED</text>
        <text x="400" y="725" font-family="monospace" font-size="14" fill="#a8a29e" text-anchor="middle">AUTH CODE: #883901</text>
        
        <!-- Decorative barcode -->
        <rect x="220" y="800" width="360" height="70" fill="#292524" rx="2"/>
        <text x="400" y="845" font-family="monospace" font-size="20" fill="#ffffff" text-anchor="middle">||| | ||||| | || ||||| |</text>
        <text x="400" y="910" font-family="monospace" font-size="15" fill="#78716c" text-anchor="middle">SAVE RECEIPT IN DONUT CONVERT</text>
      </svg>
    `)
  },
  {
    id: 'sample-doc-3',
    name: 'Architecture_Blueprint_Diagram.png',
    type: 'image/png',
    sizeBytes: 420000,
    width: 1400,
    height: 900,
    dateAdded: 'Yesterday, 04:15 PM',
    category: 'document',
    url: createSvgDataUrl(`
      <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1400 900" width="1400" height="900">
        <rect width="1400" height="900" fill="#0f172a"/>
        <!-- Blueprint Grid -->
        <defs>
          <pattern id="grid" width="40" height="40" patternUnits="userSpaceOnUse">
            <path d="M 40 0 L 0 0 0 40" fill="none" stroke="#1e293b" stroke-width="1"/>
          </pattern>
        </defs>
        <rect width="1400" height="900" fill="url(#grid)" />

        <text x="80" y="80" font-family="sans-serif" font-weight="bold" font-size="28" fill="#38bdf8">DONUT CONVERT PIPELINE ARCHITECTURE</text>
        <text x="80" y="115" font-family="sans-serif" font-size="15" fill="#94a3b8">Native Jetpack Compose + Stream-based Bitmap Encoding</text>

        <!-- Node 1: Gallery Multi-Select -->
        <rect x="80" y="240" width="240" height="160" rx="12" fill="#1e293b" stroke="#38bdf8" stroke-width="2"/>
        <circle cx="120" cy="280" r="16" fill="#38bdf8"/>
        <text x="120" y="286" font-family="sans-serif" font-weight="bold" font-size="16" fill="#0f172a" text-anchor="middle">1</text>
        <text x="150" y="286" font-family="sans-serif" font-weight="bold" font-size="18" fill="#f8fafc">Gallery View</text>
        <text x="100" y="325" font-family="sans-serif" font-size="14" fill="#94a3b8">Tap order sequence</text>
        <text x="100" y="350" font-family="sans-serif" font-size="14" fill="#94a3b8">Persistent indices</text>
        <text x="100" y="375" font-family="sans-serif" font-size="12" fill="#64748b">MainViewModel.kt</text>

        <!-- Arrow 1 to 2 -->
        <line x1="320" y1="320" x2="440" y2="320" stroke="#f97316" stroke-width="3" stroke-dasharray="6,4"/>
        <polygon points="440,320 428,312 428,328" fill="#f97316"/>

        <!-- Node 2: Radial Donut Wheel -->
        <rect x="440" y="200" width="280" height="240" rx="16" fill="#1e293b" stroke="#f97316" stroke-width="2"/>
        <circle cx="580" cy="300" r="60" fill="none" stroke="#ea580c" stroke-width="24"/>
        <circle cx="580" cy="300" r="24" fill="#0f172a"/>
        <text x="580" y="306" font-family="sans-serif" font-weight="bold" font-size="14" fill="#fdba74" text-anchor="middle">DONUT</text>
        <text x="580" y="380" font-family="sans-serif" font-weight="bold" font-size="16" fill="#f8fafc" text-anchor="middle">Radial Donut Picker</text>
        <text x="580" y="405" font-family="sans-serif" font-size="13" fill="#cbd5e1" text-anchor="middle">PDF · JPG · PNG · WEBP</text>

        <!-- Arrow 2 to 3 -->
        <line x1="720" y1="320" x2="840" y2="320" stroke="#f97316" stroke-width="3"/>
        <polygon points="840,320 828,312 828,328" fill="#f97316"/>

        <!-- Node 3: Streaming Engine -->
        <rect x="840" y="240" width="260" height="160" rx="12" fill="#1e293b" stroke="#10b981" stroke-width="2"/>
        <text x="870" y="286" font-family="sans-serif" font-weight="bold" font-size="18" fill="#f8fafc">Streaming Engine</text>
        <text x="870" y="325" font-family="sans-serif" font-size="14" fill="#94a3b8">Streams page-by-page</text>
        <text x="870" y="350" font-family="sans-serif" font-size="14" fill="#94a3b8">Zero memory spikes</text>
        <text x="870" y="375" font-family="sans-serif" font-size="12" fill="#10b981">PdfBuilder &amp; Bitmaps.kt</text>

        <!-- Arrow 3 to 4 -->
        <line x1="1100" y1="320" x2="1180" y2="320" stroke="#10b981" stroke-width="3"/>
        <polygon points="1180,320 1168,312 1168,328" fill="#10b981"/>

        <!-- Node 4: Safe Saver -->
        <rect x="1180" y="240" width="160" height="160" rx="12" fill="#047857" stroke="#34d399" stroke-width="1.5"/>
        <text x="1260" y="310" font-family="sans-serif" font-weight="bold" font-size="18" fill="#ffffff" text-anchor="middle">Safe Saver</text>
        <text x="1260" y="340" font-family="sans-serif" font-size="13" fill="#d1fae5" text-anchor="middle">Non-destructive</text>
        <text x="1260" y="365" font-family="sans-serif" font-size="12" fill="#a7f3d0" text-anchor="middle">Auto-increment</text>
      </svg>
    `)
  },
  {
    id: 'sample-doc-4',
    name: 'Field_Sunset_Mountains.jpg',
    type: 'image/jpeg',
    sizeBytes: 680000,
    width: 1600,
    height: 1000,
    dateAdded: '2 days ago',
    category: 'photo',
    url: createSvgDataUrl(`
      <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1600 1000" width="1600" height="1000">
        <defs>
          <linearGradient id="skyGrad" x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" stop-color="#1e1b4b"/>
            <stop offset="40%" stop-color="#701a75"/>
            <stop offset="70%" stop-color="#ea580c"/>
            <stop offset="100%" stop-color="#fbbf24"/>
          </linearGradient>
          <linearGradient id="lakeGrad" x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" stop-color="#c2410c"/>
            <stop offset="100%" stop-color="#0f172a"/>
          </linearGradient>
        </defs>
        <!-- Sky -->
        <rect width="1600" height="650" fill="url(#skyGrad)"/>
        <!-- Sun -->
        <circle cx="800" cy="520" r="90" fill="#fef08a" opacity="0.9" filter="drop-shadow(0 0 40px #f59e0b)"/>
        <!-- Distant Mountains -->
        <polygon points="100,650 400,320 700,650" fill="#4c0519" opacity="0.7"/>
        <polygon points="500,650 850,280 1200,650" fill="#3b0764" opacity="0.8"/>
        <polygon points="950,650 1300,340 1650,650" fill="#1e1b4b" opacity="0.85"/>
        <!-- Foreground Mountains -->
        <polygon points="0,650 250,420 550,650" fill="#18181b"/>
        <polygon points="1100,650 1420,400 1700,650" fill="#18181b"/>
        <!-- Lake Water with Reflections -->
        <rect y="650" width="1600" height="350" fill="url(#lakeGrad)"/>
        <!-- Water ripples -->
        <ellipse cx="800" cy="690" rx="160" ry="8" fill="#fef08a" opacity="0.4"/>
        <ellipse cx="780" cy="740" rx="220" ry="10" fill="#fb923c" opacity="0.3"/>
        <ellipse cx="810" cy="800" rx="300" ry="14" fill="#f97316" opacity="0.2"/>
        <text x="80" y="930" font-family="sans-serif" font-weight="bold" font-size="24" fill="#ffffff" opacity="0.9">Sunset Over Pine Ridge Basin</text>
        <text x="80" y="960" font-family="sans-serif" font-size="14" fill="#cbd5e1" opacity="0.8">RAW Capture · 48MP ISO 100 · 24mm f/2.8</text>
      </svg>
    `)
  },
  {
    id: 'sample-doc-5',
    name: 'Meeting_Notes_Handwritten.png',
    type: 'image/png',
    sizeBytes: 280000,
    width: 1000,
    height: 1400,
    dateAdded: '3 days ago',
    category: 'note',
    url: createSvgDataUrl(`
      <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1000 1400" width="1000" height="1400">
        <!-- Lined notepad page -->
        <rect width="1000" height="1400" fill="#fdfbf7"/>
        <defs>
          <pattern id="lines" width="100" height="36" patternUnits="userSpaceOnUse">
            <line x1="0" y1="36" x2="1000" y2="36" stroke="#e0e7ff" stroke-width="1.5"/>
          </pattern>
        </defs>
        <rect x="80" y="100" width="840" height="1200" fill="url(#lines)"/>
        <!-- Margin line -->
        <line x1="180" y1="80" x2="180" y2="1320" stroke="#fca5a5" stroke-width="2"/>

        <!-- Notes Content -->
        <text x="210" y="160" font-family="cursive, sans-serif" font-size="32" font-weight="bold" fill="#1e3a8a">Donut Convert v2 Ideas</text>
        <text x="210" y="235" font-family="cursive, sans-serif" font-size="22" fill="#1e293b">1. Radial wheel swipe with spring damping</text>
        <text x="210" y="307" font-family="cursive, sans-serif" font-size="22" fill="#1e293b">2. Tap order = page order (1, 2, 3 badge)</text>
        <text x="210" y="379" font-family="cursive, sans-serif" font-size="22" fill="#1e293b">3. Option sheet: One PDF vs Separate PDFs</text>
        <text x="210" y="451" font-family="cursive, sans-serif" font-size="22" fill="#1e293b">4. Floating bubble over any screen for instant drop</text>
        
        <!-- Sticky Note -->
        <rect x="520" y="580" width="340" height="280" fill="#fef08a" rx="4" filter="drop-shadow(3px 5px 12px rgba(0,0,0,0.15))" transform="rotate(-3 690 720)"/>
        <text x="550" y="640" font-family="sans-serif" font-weight="bold" font-size="18" fill="#854d0e">REMEMBER:</text>
        <text x="550" y="680" font-family="cursive, sans-serif" font-size="20" fill="#713f12">Android cannot encode HEIC</text>
        <text x="550" y="720" font-family="cursive, sans-serif" font-size="20" fill="#713f12">natively, so convert to</text>
        <text x="550" y="760" font-family="cursive, sans-serif" font-size="20" font-weight="bold" fill="#ea580c">PDF, JPG, PNG, WEBP!</text>
      </svg>
    `)
  },
  {
    id: 'sample-doc-6',
    name: 'Presentation_Slide_Metrics.jpg',
    type: 'image/jpeg',
    sizeBytes: 390000,
    width: 1600,
    height: 900,
    dateAdded: '3 days ago',
    category: 'document',
    url: createSvgDataUrl(`
      <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1600 900" width="1600" height="900">
        <rect width="1600" height="900" fill="#18181b"/>
        
        <!-- Top banner -->
        <rect x="80" y="80" width="1440" height="740" fill="#27272a" rx="20" stroke="#3f3f46" stroke-width="1.5"/>
        <text x="140" y="160" font-family="sans-serif" font-weight="800" font-size="36" fill="#f8fafc">CONVERSION SPEED &amp; COMPRESSION RATIOS</text>
        <text x="140" y="200" font-family="sans-serif" font-size="18" fill="#a1a1aa">Client-side benchmark across 1,000 document batches</text>

        <!-- KPI 1 -->
        <rect x="140" y="250" width="380" height="180" rx="14" fill="#18181b" stroke="#52525b"/>
        <text x="180" y="300" font-family="sans-serif" font-size="15" fill="#a1a1aa">Average PDF Assembly Time</text>
        <text x="180" y="360" font-family="sans-serif" font-weight="bold" font-size="44" fill="#38bdf8">142 ms</text>
        <text x="180" y="395" font-family="sans-serif" font-size="14" fill="#10b981">8.4x faster than remote APIs</text>

        <!-- KPI 2 -->
        <rect x="560" y="250" width="380" height="180" rx="14" fill="#18181b" stroke="#52525b"/>
        <text x="600" y="300" font-family="sans-serif" font-size="15" fill="#a1a1aa">Storage Size Reduction (WebP)</text>
        <text x="600" y="360" font-family="sans-serif" font-weight="bold" font-size="44" fill="#f97316">-73.4%</text>
        <text x="600" y="395" font-family="sans-serif" font-size="14" fill="#10b981">Zero perceptible visual loss</text>

        <!-- KPI 3 -->
        <rect x="980" y="250" width="460" height="180" rx="14" fill="#18181b" stroke="#52525b"/>
        <text x="1020" y="300" font-family="sans-serif" font-size="15" fill="#a1a1aa">Memory Peak In Jetpack Compose</text>
        <text x="1020" y="360" font-family="sans-serif" font-weight="bold" font-size="44" fill="#34d399">&lt; 38 MB</text>
        <text x="1020" y="395" font-family="sans-serif" font-size="14" fill="#38bdf8">Page-by-page garbage collected</text>

        <!-- Bar chart visualization -->
        <rect x="140" y="470" width="1300" height="290" rx="14" fill="#18181b" stroke="#3f3f46"/>
        <text x="180" y="520" font-family="sans-serif" font-weight="600" font-size="18" fill="#e4e4e7">Format Compression Efficiency</text>
        
        <!-- Bars -->
        <text x="180" y="575" font-family="sans-serif" font-size="15" fill="#a1a1aa">Original RAW / PNG</text>
        <rect x="360" y="555" width="800" height="28" rx="6" fill="#64748b"/>
        <text x="1180" y="575" font-family="sans-serif" font-size="15" fill="#cbd5e1">10.2 MB (100%)</text>

        <text x="180" y="630" font-family="sans-serif" font-size="15" fill="#a1a1aa">Donut PDF Standard</text>
        <rect x="360" y="610" width="420" height="28" rx="6" fill="#38bdf8"/>
        <text x="800" y="630" font-family="sans-serif" font-size="15" fill="#38bdf8">3.8 MB (37%)</text>

        <text x="180" y="685" font-family="sans-serif" font-size="15" fill="#a1a1aa">Donut WEBP High-Q</text>
        <rect x="360" y="665" width="230" height="28" rx="6" fill="#f97316"/>
        <text x="610" y="685" font-family="sans-serif" font-size="15" fill="#f97316">1.9 MB (18%)</text>
      </svg>
    `)
  }
];
