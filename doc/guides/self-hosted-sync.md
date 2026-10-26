# Syncing your devices through your own server

AccountabiliBuddy can keep your PCs and your phone in step without Google: each device writes one sealed sync file into a
folder, and any tool that keeps a folder the same on every device carries it. This guide shows how to do that with
**Syncthing** or **Nextcloud** on Windows, macOS, Linux and Android, how to use a computer you already own as an
always-on server, how to reach it safely from outside your home, and how to rent a small server (a VPS) instead.

Tool menus and download pages change. Where a step may have changed since this was written, check the tool's own docs.

## Contents

1. [How the folder method works](#1-how-the-folder-method-works)
2. [Which tool to pick](#2-which-tool-to-pick)
3. [Syncthing](#3-syncthing)
4. [Nextcloud](#4-nextcloud)
5. [Turn on the folder in AccountabiliBuddy](#5-turn-on-the-folder-in-accountabilibuddy)
6. [An always-on computer as your server](#6-an-always-on-computer-as-your-server)
7. [Reaching it from outside your home, safely](#7-reaching-it-from-outside-your-home-safely)
8. [A small rented server (VPS)](#8-a-small-rented-server-vps)
9. [When files don't arrive](#9-when-files-dont-arrive)

## 1. How the folder method works

- Each device writes **one file of its own** (`<id>.absync` on the PC; the name is an opaque id) and reads the others' files
  from the same folder. No two devices write the same file, so sync conflicts are rare.
- The files are **sealed** (AES-256-GCM). The key comes from your 40-character link code, which stays on your devices and
  never goes in the files. Whatever carries the folder, including a server you rent, sees only scrambled bytes and
  file names that mean nothing.
- AccountabiliBuddy opens **no connection** for this. It only reads and writes the folder; Syncthing or Nextcloud moves the files.
- What a sync file holds is limited (see ADR 22): focus times, profile names, sites, the day plan and a few settings. Never
  window titles, full addresses, screen text, reasons or notes.

Before you start, link your devices in AccountabiliBuddy (Devices → Link a new device on the PC; Devices → join with the
code or QR on the phone). The folder only carries files; it doesn't link devices.

## 2. Which tool to pick

| | Syncthing | Nextcloud |
|---|---|---|
| What it is | Devices sync a folder directly with each other | A server you run, with apps that sync to it |
| Needs a server? | No. Any device that's on passes files along; an always-on one helps | Yes, always on |
| Setup effort | Low | Medium to high (web server, database, HTTPS) |
| Reaching it from outside home | Built in: works through firewalls with no ports opened | You must make the server reachable (best through a VPN, section 7) |
| Android | Syncthing-Fork app, syncs a normal folder | A sync app such as Autosync for Nextcloud or FolderSync |
| Pick it if | You only want your devices in step | You already run Nextcloud or want its other features |

For most people **Syncthing is the simpler and safer choice**.

## 3. Syncthing

Syncthing runs on each device and shows its settings as a web page at `http://127.0.0.1:8384` (only on that device).
Every device has a long **Device ID**; two devices sync only after each one accepts the other. Traffic between devices is
encrypted.

### 3.1 Install it

**Windows**

1. Go to syncthing.net → Downloads. Pick one of the Windows options listed there:
   - **Syncthing Windows Setup** (an installer that can start Syncthing when you log in, or when the PC starts), or
   - **SyncTrayzor** (a tray app with a window around the web page). The original SyncTrayzor is no longer maintained;
     use the maintained fork the downloads page links to.
2. Install it and choose to start it automatically.
3. Open the web page (the tray icon, or `http://127.0.0.1:8384` in a browser).
4. Actions → Settings → GUI: set a **GUI user name and password**.

**macOS**

1. Go to syncthing.net → Downloads and get **Syncthing for macOS** (a menu bar app). Or, with Homebrew:
   `brew install syncthing`, then `brew services start syncthing`.
2. In the menu bar app, turn on **Start at login**.
3. Open the web page from the menu bar icon and set a GUI password (Actions → Settings → GUI).

**Linux**

1. Install it from your distribution (`sudo apt install syncthing`, `sudo dnf install syncthing`, `sudo pacman -S syncthing`)
   or from the Syncthing apt repository for the newest version (see apt.syncthing.net).
2. Start it as your user and at every login:
   `systemctl --user enable --now syncthing.service`
3. On a machine that should run Syncthing without anyone logged in (a server), also run:
   `sudo loginctl enable-linger $USER`
   (or use the system unit `sudo systemctl enable --now syncthing@<your user>.service` instead of step 2).
4. Open `http://127.0.0.1:8384` and set a GUI password.

**Android**

The original Syncthing Android app was discontinued at the end of 2024. Use **Syncthing-Fork** (from F-Droid or its
GitHub releases; check its page for where it is offered now).

1. Install Syncthing-Fork and allow the permissions it asks for (storage access, notifications).
2. Let it ignore battery optimisation when it asks, or it may stop in the background.
3. In its run conditions, choose when it may sync (for example "on Wi-Fi only" or "always"). Files only move while it runs.

### 3.2 Connect your devices

Do this once per pair; with an always-on device (section 6) you only connect each device to that one.

1. On device A: Actions → **Show ID**. A QR code and the long ID appear.
2. On device B: **Add Remote Device**, paste the ID (on Android scan the QR code), give it a name, Save.
3. Back on device A, a notice says device B wants to connect: click **Add Device**.

### 3.3 Share the folder

1. On the device that will be your hub (or your main PC): **Add Folder**.
   - Folder label: `AccountabiliBuddy sync`.
   - Folder path: an empty folder, e.g. `C:\Users\<you>\AccountabiliBuddy sync` or `~/AccountabiliBuddy sync`.
   - Sharing tab: tick your other devices.
   - Folder type: **Send & Receive**.
2. On each other device, accept the folder when the notice appears and choose where it lives.
   - On Android, put it in internal storage in a folder of its own, e.g. `/storage/emulated/0/AccountabiliBuddy sync`.
     Don't use the top of `Download` itself; Android doesn't let apps pick that folder.
3. Optional: Folder → Edit → File Versioning → "Simple" keeps a few old copies. Not needed for sync files.

### 3.4 Introducer (optional)

If you mark the always-on device as an **Introducer** on a new device (Edit device → Sharing → Introducer), it hands that
device its list of other devices and shared folders, so you don't have to connect every pair by hand.

## 4. Nextcloud

Nextcloud needs a server that is always on (section 6 or 8), with HTTPS. If you don't run one yet, the simplest start is
**Nextcloud All-in-One** (a Docker setup with its own instructions on nextcloud.com); it expects a domain name and
an open HTTPS port, which section 7 suggests you avoid. Running Nextcloud is a bigger job than Syncthing; pick it
only if you want it for more than this.

**Windows, macOS, Linux**

1. Install the **Nextcloud desktop client** from nextcloud.com → Install → Desktop (on Linux, also your distribution's
   package or the AppImage).
2. Log in to your server's address. The browser opens to confirm the login.
3. Choose a local folder to sync, or add a folder sync connection for just one folder:
   create `AccountabiliBuddy sync` on the server and sync it to e.g. `C:\Users\<you>\Nextcloud\AccountabiliBuddy sync`.
4. Let the client start at login (its settings → General).

**Android**

The Nextcloud app's **Auto upload** only uploads; it doesn't bring the other devices' files down, so it isn't enough
by itself. Use a two-way sync app that keeps a normal folder in step with Nextcloud:

1. Install a sync app such as **Autosync for Nextcloud** or **FolderSync** (both talk to Nextcloud over WebDAV).
2. Log in to your server (an app password from Nextcloud → Settings → Security is safer than your main password).
3. Make a **two-way** pair: the server's `AccountabiliBuddy sync` folder ↔ a phone folder such as
   `/storage/emulated/0/AccountabiliBuddy sync`.
4. Set how often it syncs and allow it to ignore battery optimisation.

## 5. Turn on the folder in AccountabiliBuddy

**PC**

1. Devices → How this PC shares → tick **Sync folder**.
2. **Choose folder…** and pick the synced folder (the `AccountabiliBuddy sync` folder itself, not its parent).
3. Pick how often: on change (instant) or every 1, 5 or 15 minutes.

**Phone**

1. Devices → **Sync folder (backup)** → switch it on.
2. Android's folder picker opens: go to the folder Syncthing-Fork or your Nextcloud sync app keeps, and tap **Use this folder**,
   then **Allow**.
3. Choose every 1, 5 or 15 minutes. The phone checks every 15 minutes, or faster while the timeline notification is pinned.

Each device writes its own file into the folder and reads the others'. Use **Sync now** on either side to check at once.

## 6. An always-on computer as your server

Any computer that stays on can be the hub: an old laptop, a desktop, a Raspberry Pi or a home NAS that runs Syncthing.
With Syncthing a hub is optional (devices sync whenever any two are on), but an always-on one means your phone gets the
PC's file even when the PC is off.

1. Install Syncthing (section 3.1) and make it start **without logging in**:
   - Windows: the Syncthing Windows Setup installer can install it to start at boot; or follow Syncthing's docs
     ("Starting Syncthing automatically" → Task Scheduler, "Run whether user is logged on or not").
   - Linux: `sudo loginctl enable-linger $USER` (section 3.1).
   - macOS: keep a user logged in with the menu bar app set to start at login.
2. Stop it from sleeping:
   - Windows: Settings → System → Power (& battery) → Screen and sleep → "When plugged in, put my device to sleep after" → **Never**.
   - macOS: System Settings → Energy (or Battery → Options on a laptop) → **Prevent automatic sleeping when the display is off**.
     Or in Terminal: `sudo pmset -a sleep 0`.
   - Linux: `sudo systemctl mask sleep.target suspend.target hibernate.target hybrid-sleep.target`.
3. Give it updates: let the operating system install updates, and turn on Syncthing's automatic upgrades
   (Settings → General) unless your package manager updates it.
4. Connect every other device to it (section 3.2), share the folder from it (section 3.3), and mark it as **Introducer**
   on the other devices (section 3.4).
5. Keep the hub's GUI password set, and leave the GUI listening on `127.0.0.1` only (Settings → GUI → GUI Listen Address).

## 7. Reaching it from outside your home, safely

**Never expose a file share directly to the internet.** That means: no port forwarding for Windows file sharing (SMB,
port 445), the Syncthing web page (port 8384), Remote Desktop (3389), or an unprotected Nextcloud. Exposed services are
scanned and attacked within minutes.

Safe ways, best first:

1. **Syncthing's own connections (nothing to set up).** Syncthing finds your devices through its global discovery
   servers and, when a direct connection isn't possible, passes data through public **relays**. Relayed data stays
   encrypted end to end; the relay can't read it (and your sync files are sealed anyway). You open **no ports**.
   Relays can be slower, which doesn't matter for files of a few KB.
2. **A mesh VPN: Tailscale, Headscale or ZeroTier.** Install it on every device and sign in to the same account; the
   devices then reach each other on private addresses from anywhere, with no ports opened. This is the right way to
   reach a Nextcloud server from outside: use the server's VPN address in the Nextcloud apps and keep the server off
   the public internet. (Tailscale is built on WireGuard and has a free personal plan; Headscale is a self-hosted
   version of its control server. Check their sites for current plans.)
3. **Your own WireGuard VPN.** Run WireGuard on the always-on computer or the router and open **one UDP port** for it.
   WireGuard answers only devices that have its key, so that one port shows nothing to strangers. More setup than
   option 2.

Optional, for Syncthing: forwarding TCP and UDP port **22000** (the sync port, not the web page) to the hub gives faster
direct connections. It's protected by device IDs and encryption, but it isn't needed; relays work without it.

If you must put Nextcloud on the public internet: HTTPS only, strong unique passwords, two-factor login, the
brute-force protection on, and updates as soon as they come out. A mesh VPN is still the better choice.

## 8. A small rented server (VPS)

A VPS is a small computer you rent by the month from a hosting company. It's always on and always reachable, so it
makes a good hub when you don't want a computer running at home.

**Size:** 1 vCPU, 1 GB RAM and 10-20 GB disk are plenty for Syncthing. Nextcloud wants 2 GB RAM or more.

**Set it up (Ubuntu LTS or Debian):**

1. Create the server with your **SSH key** (not a password).
2. Log in, make a normal user and give it sudo; then turn off root and password logins
   (`PermitRootLogin no`, `PasswordAuthentication no` in `/etc/ssh/sshd_config`, then `sudo systemctl restart ssh`).
3. Firewall: `sudo ufw allow OpenSSH`, then `sudo ufw enable`.
   - With a mesh VPN (recommended): install Tailscale (or your VPN) and open nothing else.
   - Syncthing without a VPN: `sudo ufw allow 22000/tcp` and `sudo ufw allow 22000/udp`. Never open 8384.
4. Automatic security updates: `sudo apt install unattended-upgrades` and `sudo dpkg-reconfigure -plow unattended-upgrades`.
5. Install Syncthing (section 3.1, Linux) for your user with lingering on.
6. Reach its web page only through SSH: on your PC run `ssh -L 8384:127.0.0.1:8384 <user>@<server>` and open
   `http://127.0.0.1:8384`. Set a GUI password.
7. Connect your devices to it and share the folder (sections 3.2-3.4).
8. Optional: in Syncthing, share the folder to the VPS as an **untrusted (encrypted)** device with a folder password.
   The VPS then stores the files encrypted a second time and never gets the password.

**Backups:** the server holds only copies; every device writes its own file again, so losing the server loses nothing.
Keep backups of your real data with AccountabiliBuddy's own backup (`.abbackup`). A provider snapshot is a nice extra.

## 9. When files don't arrive

1. **Linked?** Both apps must be linked to the same group (Devices shows the other device). The folder only carries files.
2. **Same folder?** On the PC, the chosen folder must be the synced folder itself. The `.absync` files sit at its top.
   Look inside: you should see one file per device.
3. **Is the sync tool up to date?** Syncthing's web page should show the folder "Up to Date" and the devices "Connected".
   Nextcloud's client and Autosync/FolderSync show their last sync and any error.
4. **Is it running on the phone?** Check Syncthing-Fork's run conditions (Wi-Fi only? charging only?) and that battery
   optimisation is off for it and for AccountabiliBuddy.
5. **Did the phone pick the right folder?** Devices → Sync folder (backup) shows the folder name. Choose it again if you
   moved the folder; Android forgets access to a folder that was moved or renamed.
6. **Press Sync now** on both sides, then wait for the sync tool to carry the file (seconds with Syncthing on the same
   network; longer through relays or on a sync app's schedule).
7. **Clock right?** A device whose clock is far off can make the newest file look old. Let each device set its time
   automatically.
8. **Errors in AccountabiliBuddy?** The PC's Devices tab and the phone's Sync folder section show the last problem
   (for example "can't write to the folder").
9. **Conflict copies?** Syncthing names them `*.sync-conflict-*`; they can be deleted. They're rare, since each device
   writes only its own file.
