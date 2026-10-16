// Block page shown when the app sends a tab here. MV3 forbids inline scripts, hence this file.

const api = globalThis.browser ?? globalThis.chrome;
const params = new URLSearchParams(location.search);

document.getElementById("reason").textContent = params.get("reason") || "This page isn't part of your focus session.";
document.getElementById("url").textContent = params.get("url") || "";

async function closeTab() {
  const tab = await api.tabs.getCurrent();
  if (tab?.id !== undefined) {
    await api.tabs.remove(tab.id);
  }
}

document.getElementById("back").addEventListener("click", async () => {
  // History is [..., page before, blocked page, this page]; skip the blocked page.
  if (history.length > 2) {
    history.go(-2);
  } else {
    await closeTab();
  }
});

document.getElementById("close").addEventListener("click", closeTab);
