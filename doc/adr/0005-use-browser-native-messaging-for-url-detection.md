# 5. Use browser native messaging for URL detection

Date: 2026-10-15

## Status

Accepted

## Context

Window titles alone cannot reliably tell which site or page a browser is showing, and distinguishing views inside a site (a tutorial video versus Shorts) needs the URL. The app must support Chrome, Brave and Firefox at minimum, plus Edge and Opera, which covers the Chromium and Gecko engines. Reading the address bar through UI Automation is brittle across browser versions. A browser extension can report URLs reliably and can also block or redirect tabs, but it needs a channel to the desktop app.

## Decision

We will ship one browser extension (Manifest V3 for Chromium browsers, with a Firefox manifest variant) that reports tab URLs and receives block instructions. It will communicate through browser native messaging with a small `AccountabiliBuddy.NativeHost` executable, which relays messages to the desktop app over a local named pipe. We chose this over a localhost HTTP or WebSocket server because any local process could connect to such a server, while native messaging is only available to the registered extension.

## Consequences

URL detection and tab blocking are reliable, and no network port is opened. In exchange, users must install the extension in each browser, the host must be registered under each browser's own native messaging registry key (Brave and Opera differ from Chrome), and the extension must be maintained for both engines. Safari is not covered.
