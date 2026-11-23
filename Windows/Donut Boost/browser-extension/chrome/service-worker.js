const MENU_LINK = "donutboost-send-link";
const MENU_PAGE = "donutboost-send-page";

chrome.runtime.onInstalled.addListener(() => {
  chrome.contextMenus.removeAll(() => {
    chrome.contextMenus.create({
      id: MENU_LINK,
      title: "Send link to Donut Boost",
      contexts: ["link"],
    });
    chrome.contextMenus.create({
      id: MENU_PAGE,
      title: "Send this page to Donut Boost",
      contexts: ["page"],
      documentUrlPatterns: ["http://*/*", "https://*/*"],
    });
  });
});

chrome.contextMenus.onClicked.addListener(async (info) => {
  const value = info.menuItemId === MENU_LINK ? info.linkUrl
    : info.menuItemId === MENU_PAGE ? info.pageUrl : null;
  if (!value || !/^(https?:\/\/|magnet:)/i.test(value)) return;

  try {
    const handoff = `donutboost://download?url=${encodeURIComponent(value)}`;
    if (handoff.length > 8192) {
      console.warn("The link is too long to send to Donut Boost.");
      return;
    }
    await chrome.tabs.create({ url: handoff, active: false });
  } catch {
    console.warn("Could not open Donut Boost. Check that the Windows app is installed.");
  }
});
