# 49. Themes from token dictionaries, with bundled fonts and a local sunset

Date: 2026-10-28

## Status

Accepted

## Context

The PC app has no theme of its own. It runs on Avalonia's FluentTheme, and each view paints its cards with hard-coded tints (`#1A2A9D8F`, `#1AF4A261` and many more). The Claude Design redesign of 2026-10-27/28 (handoff in `handoffContext/mockups/redesign/`) gives every device one layout and three themes: Instrument (the default), Night Desk · Calm and Paper. Each theme has a light and a dark version. A theme may change only colour tokens, the display and numeral fonts, card edges and corner radii. It never changes layout, icons or type sizes.

The design names fonts Windows doesn't ship: IBM Plex Mono for Instrument's numerals, Manrope for Night Desk and Newsreader for Paper. On top of Light, Dark and System, the user wants light/dark to follow a time they set or the sunset. Sunset needs a location. The app is local only: it may not look anything up online, and private data stays on the device.

## Decision

We will build the PC look from token resource dictionaries. There is one `ResourceDictionary` per theme under `src/AccountabiliBuddy.App/Themes/`, each with Light and Dark `ThemeDictionaries` holding the same keys (`Ab.Bg`, `Ab.Surface`, `Ab.Ink`, `Ab.Accent`, `Ab.DisplayFont`, `Ab.CardRadius` and so on). A small override dictionary maps FluentTheme's own accent and background resources onto those tokens, so pages that haven't been redesigned yet still follow the theme. Views refer to tokens through `DynamicResource` and never to raw colours. A `ThemeService` swaps the theme dictionary at runtime and sets the light or dark variant.

We will bundle the three fonts (IBM Plex Mono, Manrope and Newsreader, all under the SIL Open Font License) as app assets, with their licence texts. Body text stays on the system's Segoe UI Variable.

The light/dark choice is Light, Dark, System, At a set time, or After sunset. For sunset the app works out sunrise and sunset itself, from a pure formula in Core, using a location from one of three places:
- Windows location, through a Core port with a Windows adapter, at coarse accuracy and read at most once a day;
- a typed latitude and longitude;
- a city picked from a bundled GeoNames city list (CC BY, attribution in About), searched on the device.

The location is private. It is stored DPAPI-encrypted in the `documents` table, never synced and never sent anywhere. The theme choice is per device for now.

## Consequences

The design's three themes become data, not code. A later theme, or a fix to a colour, is a dictionary edit, and Android can mirror the same token names in its `Theme.kt`. Moving the remaining views off hard-coded tints is now a known, mechanical job for each page's redesign part. Until that is done, those pages mix the new ground with their old tints.

The app grows by the three font files (about 0.5 MB) and the city list (about 1–2 MB), and both licences must stay credited. Using Windows location adds a permission prompt and a new kind of private data to protect. The hard rules record it next to the other private data. Theme switching driven by time and sunset is one more scheduled thing that has to follow `TrustedClock`.

Going back to FluentTheme defaults would be cheap. Undoing the token names once every view uses them would not be.
