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
- **Game Launcher Monitor:** Detects selected launcher process activity and reports physical-adapter receive rates. Verified game size/progress is currently available only when Steam exposes active manifest counters; other launchers do not have a verified progress provider in this build. Launcher traffic stays on Windows network routing and is not bonded by this monitor.
- **System Proxy Safety:** Automatic registry proxy cleanup on application close, exit, or crash to ensure Windows network settings are never left modified.
- **Native Windows WPF:** Built on .NET 8 for fast, smooth performance and low CPU/RAM usage.

## Torrent downloads

Paste a BitTorrent magnet link into the download field, or use **Torrent** to choose a `.torrent` file. An HTTP(S) link ending in `.torrent` also works. **Combine connections for torrent peers** assigns outgoing TCP peers to enabled adapters and closes their connections when an adapter is switched off. Tracker and DHT discovery may use the Windows route. Clear the option before starting for the original Windows routing, incoming peers, and HTTP web seeds. Donut Boost shows verified progress and stops the torrent when it completes.

## Download queue

Use **Add to queue** for a direct link, magnet link, or torrent file. Leave the time blank to start when the current download finishes, or enter a local time in `HH:mm` format. Scheduled downloads start while the app is open. The queue and its history are saved locally. After an interruption, **Retry selected** restarts a direct transfer or rechecks the partial torrent data.

## Send links from Chrome or Edge

Click **Browser extension** in Donut Boost to open the folder bundled with the installed app. In Chrome or Edge, open `chrome://extensions` or `edge://extensions`, enable **Developer mode**, choose **Load unpacked**, and select that folder. App updates replace the bundled extension files. Use **Reload** on the extension card or **Reload extension** in the pinned toolbar panel to apply them.

Right-click a direct download or magnet link, or paste it into the pinned panel. The extension opens Donut Boost and fills its download box. Review the link and press **Boost Download** to start. The handoff sends only the URL, so links that depend on browser cookies may fail.
