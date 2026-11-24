async function sendToDonutBoost(value, tabId) {
  const link = value?.trim() ?? "";
  if (!/^(https?:\/\/|magnet:\?)/i.test(link) || /[\x00-\x1f\x7f]/.test(link)) {
    throw new Error("Enter a direct download or magnet link.");
  }
  let parsed;
  try {
    parsed = new URL(link);
  } catch {
    throw new Error("Enter a valid direct download or magnet link.");
  }
  if (parsed.protocol === "magnet:" && !parsed.searchParams.get("xt")) {
    throw new Error("Enter a valid direct download or magnet link.");
  }
  const handoff = `donutboost://download?url=${encodeURIComponent(link)}`;
  if (handoff.length > 8192) throw new Error("This link is too long to send. Paste it into Donut Boost.");
  if (!Number.isInteger(tabId) || tabId < 0) throw new Error("Open a browser tab and try again.");

  try {
    await chrome.tabs.update(tabId, { url: handoff });
  } catch {
    throw new Error("Chrome could not open Donut Boost. Check that the Windows app is installed.");
  }
}
