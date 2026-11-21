# ⚡ Donut Boost — Windows Multi-Connection Speed Bonding Downloader

**Donut Boost** is a high-performance Windows desktop application designed to bond multiple internet connections (Home Wi-Fi + multiple 4G/5G mobile phones via USB Tethering) into a single aggregated high-speed download pipeline.

---

## 🚀 How It Works

Traditional applications route all traffic through a single default gateway. **Donut Boost** bypasses this by using **Multi-Socket Network Interface Binding**:

1. **Hardware Discovery:** Detects every active network card on Windows (Wi-Fi, Ethernet, and phone USB tethering adapters like NDIS/RNDIS).
2. **Dedicated Sockets:** Every chunk request binds directly to a specific physical adapter's local IP using `Socket.Bind(new IPEndPoint(adapterIp, 0))`.
3. **Dynamic Parallel Slicing:** When downloading a large file (games, videos, ISOs, zip archives), the file is divided into byte-range chunks. 
4. **Auto-Balancing:** Faster connections (e.g. 500 Mbps Jio 5G) finish chunks faster and automatically pull more work from the queue, while 300 Mbps Wi-Fi pulls concurrently.

### 📊 Theoretical Throughput:
| Connection | Typical Speed |
|---|---|
| Home Wi-Fi / Fiber | ~300 Mbps |
| Phone 1 (Jio Unlimited 5G USB Tethering) | ~500 Mbps |
| Phone 2 (5G USB Tethering) | ~500 Mbps |
| **Combined Donut Boost Speed** | **~1.3 Gbps (1300 Mbps!)** |

---

## 🔌 How to Use

1. **Connect to your Home Wi-Fi.**
2. **Plug in Phone 1 via USB:** On Android, go to **Settings → Hotspot & Tethering → Turn ON "USB Tethering"**.
3. **Plug in Phone 2 via USB:** Turn on **USB Tethering**.
4. Open **`DonutBoost.exe`** (located in `publish/win-x64/` or run from Visual Studio / `dotnet run`).
5. Notice all your connections appear in the **"Bonded Internet Connections"** panel on the right.
6. Paste any download URL and click **⚡ BOOST DOWNLOAD**!

---

## 🛠️ Features
- **Live Speedometer:** Circular real-time gauge displaying combined throughput in Mbps / Gbps.
- **Bandwidth Speed Limiter:** Real-time rate throttling with presets (Unlimited, 50 Mbps, 100 Mbps, 200 Mbps) or custom Mbps caps adjustable on the fly mid-download.
- **Per-Adapter Metrics:** See real-time bandwidth contribution of each phone and Wi-Fi connection.
- **Mid-Download Switching:** Dedicated ON/OFF toggles allow disabling or enabling adapters mid-stream without resetting progress.
- **Resilient Recovery:** If a phone gets disconnected during a download, in-flight chunks are recovered and seamlessly redirected to the remaining active connections.
- **Game Launcher Accelerator:** Built-in multi-interface proxy engine automatically bonds connections for Steam, Epic Games, EA App, Rockstar, and Battle.net without requiring URL pasting.
- **System Proxy Safety:** Automatic registry proxy cleanup on application close, exit, or crash to ensure Windows network settings are never left modified.
- **Native Windows WPF:** Built on .NET 8 for fast, smooth performance and low CPU/RAM usage.
