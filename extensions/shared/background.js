// AccountabiliBuddy extension: reports the active tab of every window to the desktop app and
// sends a tab to the block page when the app asks. Talks only to the local native messaging host (ADR 5).
// Runs as a service worker in Chromium browsers and as an event page in Firefox.

const api = globalThis.browser ?? globalThis.chrome;

const HOST_NAME = "com.accountabilibuddy.host";
const RECONNECT_DELAY_MS = 5000;
const SEND_DEBOUNCE_MS = 150;

let port = null;
let appConnected = false;
let sendTimer = null;

// Channel name per tab, reported by the YouTube content script.
const channelByTab = new Map();

async function detectBrowser() {
  if (api.runtime.getBrowserInfo) {
    return "firefox";
  }
  try {
    if (navigator.brave && (await navigator.brave.isBrave())) {
      return "brave";
    }
  } catch {
    // Not Brave.
  }
  if (navigator.userAgent.includes("Edg/")) {
    return "edge";
  }
  const brands = navigator.userAgentData?.brands ?? [];
  if (brands.some((entry) => entry.brand === "Google Chrome")) {
    return "chrome";
  }
  return "chromium";
}

function connect() {
  try {
    port = api.runtime.connectNative(HOST_NAME);
  } catch {
    port = null;
    setTimeout(connect, RECONNECT_DELAY_MS);
    return;
  }

  port.onMessage.addListener(onHostMessage);
  port.onDisconnect.addListener(() => {
    // Host missing (not granted in the app's Setup tab) or it exited; try again later.
    port = null;
    appConnected = false;
    setTimeout(connect, RECONNECT_DELAY_MS);
  });
}

async function onHostMessage(message) {
  if (message?.type === "hostStatus") {
    appConnected = message.appConnected === true;
    if (appConnected) {
      post({ type: "hello", browser: await detectBrowser(), version: api.runtime.getManifest().version });
      await sendTabs();
    }
    return;
  }

  if (message?.type === "redirect" && Number.isInteger(message.tabId)) {
    await redirectToBlockPage(message.tabId, String(message.reason ?? ""));
  }
}

function post(message) {
  if (port && appConnected) {
    try {
      port.postMessage(message);
    } catch {
      // Port closed between the check and the call; onDisconnect reconnects.
    }
  }
}

function scheduleSend() {
  clearTimeout(sendTimer);
  sendTimer = setTimeout(sendTabs, SEND_DEBOUNCE_MS);
}

async function sendTabs() {
  if (!appConnected) {
    return;
  }
  const tabs = await api.tabs.query({ active: true });
  post({
    type: "tabs",
    tabs: tabs.map((tab) => ({
      tabId: tab.id,
      windowId: tab.windowId,
      title: tab.title ?? "",
      url: tab.url ?? "",
      channel: channelByTab.get(tab.id) ?? null,
    })),
  });
}

async function redirectToBlockPage(tabId, reason) {
  let tab;
  try {
    tab = await api.tabs.get(tabId);
  } catch {
    return; // Tab already closed.
  }

  const ownPages = api.runtime.getURL("");
  if (!tab.url || tab.url.startsWith(ownPages)) {
    return;
  }

  const params = new URLSearchParams({ url: tab.url, reason });
  await api.tabs.update(tabId, { url: `${api.runtime.getURL("blocked.html")}?${params}` });
}

api.runtime.onMessage.addListener((message, sender) => {
  if (message?.type === "channel" && sender.tab?.id !== undefined) {
    const previous = channelByTab.get(sender.tab.id) ?? null;
    const channel = typeof message.channel === "string" && message.channel.length > 0 ? message.channel : null;
    channelByTab.set(sender.tab.id, channel);
    if (channel !== previous) {
      scheduleSend();
    }
  }
});

api.tabs.onActivated.addListener(scheduleSend);
api.tabs.onUpdated.addListener((tabId, changeInfo) => {
  if (changeInfo.url !== undefined) {
    // A new page; its channel (if any) is reported again by the content script.
    channelByTab.delete(tabId);
  }
  if (changeInfo.url !== undefined || changeInfo.title !== undefined || changeInfo.status === "complete") {
    scheduleSend();
  }
});
api.tabs.onRemoved.addListener((tabId) => {
  channelByTab.delete(tabId);
  scheduleSend();
});
api.windows.onFocusChanged.addListener(scheduleSend);
api.windows.onRemoved.addListener(scheduleSend);

connect();
