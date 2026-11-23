async function sendToDonutBoost(value) {
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

  try {
    await chrome.tabs.create({ url: handoff, active: false });
  } catch {
    throw new Error("Chrome could not open Donut Boost. Check that the Windows app is installed.");
  }
}
