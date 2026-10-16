// Reports the channel of the YouTube video or channel page in this tab, so profiles can allow or block
// specific channels. YouTube changes its page structure from time to time; the selectors below are best effort.

const api = globalThis.browser ?? globalThis.chrome;

const CHECK_INTERVAL_MS = 2000;
const RESEND_INTERVAL_MS = 15000;

const VIDEO_OWNER_SELECTORS = [
  "ytd-watch-metadata ytd-channel-name a",
  "#owner #channel-name a",
  "ytd-reel-video-renderer[is-active] #channel-name a",
  "ytm-slim-owner-renderer .slim-owner-channel-name",
];

const CHANNEL_PAGE_SELECTORS = [
  "yt-page-header-renderer h1",
  "ytd-c4-tabbed-header-renderer #channel-name #text",
  "#channel-header #channel-name #text",
];

let lastChannel;
let lastSentAt = 0;

function firstText(selectors) {
  for (const selector of selectors) {
    const text = document.querySelector(selector)?.textContent?.trim();
    if (text) {
      return text;
    }
  }
  return null;
}

function readChannel() {
  const path = location.pathname;
  if (path.startsWith("/watch") || path.startsWith("/shorts")) {
    return (
      firstText(VIDEO_OWNER_SELECTORS) ??
      document.querySelector('span[itemprop="author"] link[itemprop="name"]')?.getAttribute("content") ??
      null
    );
  }
  if (path.startsWith("/@") || path.startsWith("/channel/") || path.startsWith("/c/")) {
    return firstText(CHANNEL_PAGE_SELECTORS);
  }
  return null;
}

function report() {
  if (document.visibilityState !== "visible") {
    return;
  }
  const channel = readChannel();
  const now = Date.now();
  // Resend now and then: the background script forgets its state when the browser suspends it.
  if (channel !== lastChannel || now - lastSentAt > RESEND_INTERVAL_MS) {
    lastChannel = channel;
    lastSentAt = now;
    api.runtime.sendMessage({ type: "channel", channel }).catch(() => {});
  }
}

document.addEventListener("yt-navigate-finish", () => setTimeout(report, 500));
document.addEventListener("visibilitychange", report);
setInterval(report, CHECK_INTERVAL_MS);
report();
