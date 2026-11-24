const CAPTURE_HOST = "com.donutboost.browser";
const CAPTURE_ALARM = "donutboost-check-browser-downloads";
const CAPTURE_PREFIX = "browser-download-";
const CAPTURE_DEFAULTS = { enabled: false, minimumMb: 10, excludedSites: [] };
const captureInProgress = new Set();
let checkingCaptures = false;

function captureKey(id) {
  return CAPTURE_PREFIX + id;
}

function eligibleForCapture(item, settings) {
  if (!settings.enabled || item.incognito || item.byExtensionId || item.paused ||
      item.state !== "in_progress" || item.danger !== "safe" ||
      !Number.isSafeInteger(item.totalBytes) || item.totalBytes <= 0 ||
      item.totalBytes < settings.minimumMb * 1_000_000) return false;

  const urls = [item.url, item.finalUrl, item.referrer].filter(Boolean);
  try {
    const download = new URL(item.finalUrl || item.url);
    if (!["https:", "http:"].includes(download.protocol) || download.username || download.password ||
        download.pathname.toLowerCase().endsWith(".torrent")) return false;
    return !urls.some(value => {
      const host = new URL(value).hostname.toLowerCase().replace(/\.$/, "");
      return settings.excludedSites.some(site => host === site || host.endsWith("." + site));
    });
  } catch {
    return false;
  }
}

async function appCaptureRequest(message) {
  try {
    return (await chrome.runtime.sendNativeMessage(CAPTURE_HOST, message))?.status ?? "unavailable";
  } catch {
    return "unavailable";
  }
}

async function restoreBrowserDownload(id) {
  const [item] = await chrome.downloads.search({ id });
  if (item?.state === "in_progress" && item.paused)
    await chrome.downloads.resume(id);
}

async function captureBrowserDownload(item) {
  const id = item.id;
  if (captureInProgress.has(id)) return;
  captureInProgress.add(id);
  const key = captureKey(id);
  try {
    const stored = await chrome.storage.local.get(["captureSettings", key]);
    if (stored[key] || !eligibleForCapture(item, stored.captureSettings ?? CAPTURE_DEFAULTS)) return;
    if (!await chrome.permissions.contains({ permissions: ["downloads", "nativeMessaging"] })) return;
    const transferId = crypto.randomUUID();
    await chrome.alarms.create(CAPTURE_ALARM, { periodInMinutes: 0.5 });
    await chrome.storage.local.set({ [key]: { transferId, startedAt: Date.now() } });
    try {
      await chrome.downloads.pause(id);
      const [paused] = await chrome.downloads.search({ id });
      if (paused?.state !== "in_progress" || !paused.paused) {
        await chrome.storage.local.remove(key);
        return;
      }
      const status = await appCaptureRequest({
        action: "queue", id: transferId, url: paused.finalUrl || paused.url, expectedBytes: item.totalBytes
      });
      if (status === "accepted" || status === "completed") return;
      await restoreBrowserDownload(id);
      await chrome.storage.local.remove(key);
    } catch {
      try {
        await restoreBrowserDownload(id);
        await chrome.storage.local.remove(key);
      } catch { /* Background check will retry restoring the browser download. */ }
    }
  } finally {
    captureInProgress.delete(id);
  }
}

async function checkBrowserCaptures() {
  if (checkingCaptures || !chrome.downloads) return;
  checkingCaptures = true;
  try {
    const records = Object.entries(await chrome.storage.local.get(null)).filter(([key]) => key.startsWith(CAPTURE_PREFIX));
    if (records.length === 0) {
      await chrome.alarms.clear(CAPTURE_ALARM);
      return;
    }
    for (const [key, record] of records) {
      const id = Number(key.slice(CAPTURE_PREFIX.length));
      if (captureInProgress.has(id)) continue;
      const [item] = await chrome.downloads.search({ id });
      if (item?.state !== "in_progress" || !item.paused) {
        await chrome.storage.local.remove(key);
        continue;
      }
      const status = await appCaptureRequest({ action: "status", id: record.transferId });
      if (status === "accepted") continue;
      if (status === "completed") await chrome.downloads.cancel(id);
      else if (status === "unavailable" && Date.now() - record.startedAt < 60_000) continue;
      else await restoreBrowserDownload(id);
      await chrome.storage.local.remove(key);
    }
  } catch (error) {
    console.warn("Donut Boost could not update a captured browser download.", error.message);
  } finally {
    checkingCaptures = false;
  }
}

function onBrowserDownload(item) {
  captureBrowserDownload(item).catch(error => console.warn("Donut Boost capture:", error.message));
}

function onBrowserDownloadChanged(change) {
  if (change.totalBytes?.current > 0)
    chrome.downloads.search({ id: change.id }).then(items => {
      if (items[0]) onBrowserDownload(items[0]);
    }).catch(() => {});
}

function registerCaptureListeners() {
  if (!chrome.downloads || chrome.downloads.onCreated.hasListener(onBrowserDownload)) return;
  chrome.downloads.onCreated.addListener(onBrowserDownload);
  chrome.downloads.onChanged.addListener(onBrowserDownloadChanged);
}

registerCaptureListeners();
chrome.permissions.onAdded.addListener(registerCaptureListeners);
chrome.alarms.onAlarm.addListener(alarm => {
  if (alarm.name === CAPTURE_ALARM) void checkBrowserCaptures();
});
chrome.runtime.onStartup.addListener(() => {
  chrome.alarms.create(CAPTURE_ALARM, { periodInMinutes: 0.5 }).catch(() => {});
});
chrome.runtime.onInstalled.addListener(() => {
  chrome.alarms.create(CAPTURE_ALARM, { periodInMinutes: 0.5 }).catch(() => {});
});
