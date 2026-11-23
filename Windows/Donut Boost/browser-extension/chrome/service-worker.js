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

chrome.contextMenus.onClicked.addListener((info) => {
  const value = info.menuItemId === MENU_LINK ? info.linkUrl : info.pageUrl;
  if (!value || !/^(https?:\/\/|magnet:)/i.test(value)) return;
  const handoff = `donutboost://download?url=${encodeURIComponent(value)}`;
  chrome.tabs.create({ url: handoff, active: false });
});
