# Syncing your devices through your own server

AccountabiliBuddy can keep your PCs and your phone in step without Google: each device writes one sealed sync file into a
folder, and any tool that keeps a folder the same on every device carries it. This guide shows how to do that with
**Syncthing** or **Nextcloud** on Windows, macOS, Linux and Android, how to use a computer you already own as an
always-on server, how to reach it safely from outside your home, and how to rent a small server (a VPS) instead.

There is a second way, the **own relay** (section 10): a small program of ours that you run on a computer or server. Devices
put their sealed files on it and it tells the others at once, so a change arrives in about a second instead of after a
sync tool's next round. Sections 1-9 are the folder method; section 10 is the relay.

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
10. [Own relay: push in about a second](#10-own-relay-push-in-about-a-second)

## 1. How the folder method works

- Each device writes **one file of its own** (`<id>.absync` on the PC; the name is an opaque id) and reads the others' files
  from the same folder. No two devices write the same file, so sync conflicts are rare.
- The files are **sealed** (AES-256-GCM). The key comes from your 40-character link code, which stays on your devices and
  never goes in the files. Whatever carries the folder, including a server you rent, sees only scrambled bytes and
  file names that mean nothing.
- With the folder method AccountabiliBuddy opens **no connection**. It only reads and writes the folder; Syncthing or
  Nextcloud moves the files. (The own relay in section 10 is different: the app itself connects, over HTTPS, to the one
  address you give it, and only while that method is switched on.)
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

For most people **Syncthing is the simpler and safer choice** for the folder method. If you want changes to arrive in
about a second, or you have no tool like these on the phone, the **own relay** (section 10) is built for that.

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
8. **Errors in AccountabiliBuddy?** The PC's Settings › Devices & sync and the phone's Sync folder section show the last problem
   (for example "can't write to the folder").
9. **Conflict copies?** Syncthing names them `*.sync-conflict-*`; they can be deleted. They're rare, since each device
   writes only its own file.

## 10. Own relay: push in about a second

The **relay** is a small program (`AccountabiliBuddy.Relay`) you run on a computer or a rented server. Each device puts
its own sealed sync file on it (the same file the folder method writes) and the relay tells the other devices the moment
a file changes, so a phone action reaches the PC in about a second. No Google account and no sync tool are needed. It
can run next to the other methods; the files are the same, so Drive, a folder and the relay can carry each other's.

### 10.1 What the relay can and can't see

| The relay can see | The relay can't see |
|---|---|
| That a group exists, under an id that means nothing (it comes from your link code but can't be turned back into it) | The contents of any file: focus times, profiles, sites, the day plan |
| The names of the files (opaque ids), their sizes and when they changed | Your link code, or the key that opens the files |
| The address each device connects from, and when (so it can tell a phone's pattern from a PC's) | What the devices are called, or anything about their owner |
| How many devices write to a group: one stable file name per device, its size and write times | |
| A hash of the group's token (to tell whether a device belongs to the group) | |

How it is protected:

- **Sealed end to end.** Each device seals its file with AES-256-GCM before it leaves the device. The key is derived from
  your 40-character link code with HKDF-SHA256 and never leaves your devices. The relay, your network and a host that
  rents you the server only ever carry scrambled bytes.
- **HTTPS only**, as a second layer that also hides the file names and the token from the network. Either the relay
  makes its own certificate and the apps **pin** it (they check the certificate's fingerprint, and refuse to talk
  to anything else), or a normal certificate from Let's Encrypt is checked the usual way.
- **A bearer token** (another value derived from the link code, and from the relay's own certificate pin or address, so
  it is different on every relay) proves a device belongs to the group. The first device to write a group owns it on
  that relay; any other token gets "403 Forbidden". Rejoining with the same code on the same relay gives the same
  token. A new group (a new code) is a new, separate group. A relay that learns its token can't use it on another relay.
- **An admission key** (made by the relay on its first start, kept next to its certificate) is needed to start a **new**
  group on it. Without that, anyone who could reach the relay could claim groups and store files on it. A group that
  already exists needs only its own token, so the devices of your group never need the key again.
- **Limits** bound what one address can do: files up to 4 MiB, 64 files per group, files untouched for 30 days are
  deleted, 120 requests a minute per address (an IPv6 address counts as its whole /64, the block one connection
  gets), a few new groups per address per day, and a cap on the number of groups (`--max-groups`, default 16). They are
  a backstop. **Without the admission key, anyone who can reach a relay could store files on it**; the key is what
  stops that, so keep it as private as the link code.

What it does not do:

- There is no key rotation: someone who gets your link code can read everything sealed with it, including old files
  still on the relay (they stay up to 30 days after the last write). If a code leaks, make a new group on every device
  (Devices → Leave group, then Link a device).
- A lost or stolen device is removed only by making a new group. Every device in a group can write any other device's
  file and delete it, because they share one key.
- The relay can't read or change the contents of a file (a changed file doesn't open). It **can** hold files back, delete
  them, and serve an older copy of a file; a device then just doesn't see a newer change (Settings › Devices & sync shows
  "Behind by …" for a device whose file is old). Anyone who has the group's token on that relay (a hostile relay that
  saw it, for example) can also overwrite or delete the group's files there.
- **The join QR and the link code are the key to all your data.** Never photograph, screen-share or post them, and
  never open a join link that you did not just make yourself on your own device. A link from a message or a web page
  can move your phone into someone else's group and send its data to their server; the phone asks before it does
  (and asks twice before leaving your current group), but only you can tell whose link it is.

### 10.2 Get the relay program

The relay is built from AccountabiliBuddy's source (you need the .NET 10 SDK), or you can use Docker.

- Self-contained builds (nothing to install on the machine that runs them): `pwsh scripts/publish-relay.ps1` makes
  `publish\relay\win-x64`, `linux-x64` and `linux-arm64`.
- Docker: from the source folder `deploy/relay`, run `docker compose up -d --build`.

Options: `--urls <https address>` (default `https://0.0.0.0:8743`), `--data <folder>` (where it keeps the files and its
certificate and admission key), `--proxy` (plain HTTP on 127.0.0.1 for a reverse proxy, section 10.4),
`--max-disk-mb <n>` (default 2048), `--max-groups <n>` (default 16) and `--version` (prints the build and the .NET
runtime it runs on, then exits; the same line is printed every time the relay starts).

On its first start without `--proxy` it makes a self-signed certificate (`relay.pfx` in the data folder, valid 10 years,
no names in it) and, with or without `--proxy`, an admission key (`relay-admission.key`). It prints, and keeps in
`relay-address-and-pin.txt` in the data folder, lines like:

```
relay address: https://192.168.1.20:8743   pin: ZAgybCIdjRsEetCcRP0UHQ9oJRvHalRvDK-ih1-by1I
admission key: 3q2-7wXk0b1v9c5lYtPzJq8mRnHdQ4aUe6sGfBoTiVw   (a device needs it to start a new group here; ...)
```

The pin is the same for every address of that relay. Deleting `relay.pfx` makes a new certificate and a new pin, and
every device must then be given the new pin. Deleting `relay-admission.key` makes a new key (devices that already have a
group keep working; a device that still has to start a group needs the new key). The data folder is made readable only by
the account that runs the relay; if you make the folder yourself, do the same (on Windows, remove "Users" from its
permissions), because it holds the certificate's private key and the key.

The in-app relay (Devices → Run a relay on this PC) knows its own key and puts it in the join QR by itself; you only
need to copy the key by hand for a relay that runs somewhere else.

### 10.3 At home, and away from home through a VPN (pinned certificate)

This is the simplest and safest setup: the relay is never on the public internet.

1. Run the relay on a computer that stays on at home (section 6 has the sleep settings).
   - **Windows:** in an administrator PowerShell, from the source folder:
     `pwsh deploy/relay/install-windows-task.ps1 -ExePath .\publish\relay\win-x64\AccountabiliBuddy.Relay.exe -OpenFirewall`.
     It runs the relay at startup under a service account and opens the port on Private and Domain networks only.
     The address, pin and admission key are in `C:\ProgramData\AccountabiliBuddyRelay\relay-address-and-pin.txt`.
   - **Linux:** copy the linux build to `/opt/accountabilibuddy-relay/`, copy `deploy/relay/accountabilibuddy-relay.service`
     to `/etc/systemd/system/` and run `sudo systemctl enable --now accountabilibuddy-relay` (the unit's comments have the
     user and folder to make first). Read the pin and the admission key with `journalctl -u accountabilibuddy-relay`.
   - **Docker:** `docker compose up -d --build` in `deploy/relay`, then `docker compose logs relay`. In Docker the printed
     names are the container's own: use your host's address with port 8743 and the printed pin. Note that Docker publishes
     the port around `ufw` and most host firewalls, so in this mode 8743 is open to every network the machine is on.
2. At home, devices on the same network use the relay's LAN address (`https://192.168.1.20:8743`). Give that computer a
   fixed address in your router so it doesn't change.
3. Away from home, put the relay's computer and your phone on a **mesh VPN** such as Tailscale (or your own WireGuard,
   section 7) and use the relay's VPN address or name instead. Open nothing on your router.
4. In the apps, type the address, the **pin** and the **admission key** exactly as printed. The pin is what proves it is your relay; the
   certificate's name doesn't have to match the address.

If the pin ever doesn't match, the apps stop and say so. They never fall back to an unchecked connection.

### 10.4 On the public internet: Caddy and Let's Encrypt

Use this when you want to reach the relay from anywhere without a VPN. It needs a DNS name pointing at your server.

1. Get a DNS name (for example `relay.example.com`) with an A record to the server's public address.
2. Run the relay behind a reverse proxy: start it with `--proxy`, so it listens on plain HTTP at `127.0.0.1:8743` only.
   (Docker: use the "proxy" lines in `docker-compose.yml`. systemd: add `--proxy` to `ExecStart`.)
3. Install **Caddy** and use `deploy/relay/Caddyfile` with your name. Caddy gets and renews the Let's Encrypt
   certificate by itself and forwards to the relay. Ports **80** (only for the certificate check) and **443** must reach
   the server. The Caddyfile already sends the push stream through without buffering and limits bodies to 5 MB.
4. In the apps type `https://relay.example.com`, **leave the pin empty** (the certificate is checked normally) and enter
   the **admission key** the relay printed.

The relay tells clients apart by the **last** `X-Forwarded-For` entry, which the proxy must set itself. Caddy does that
by default. With another proxy, replace the header with the real client address, never append to the one the client
sent (nginx: `proxy_set_header X-Forwarded-For $remote_addr;`); otherwise anyone can dodge the per-address limits.

**Keep the server patched, and the relay and Caddy too** (section 10.7: the operating system's updates do not update the relay, and Caddy is the part that faces the internet: update it with `apt upgrade` or `caddy upgrade`).
Use Caddy 2.5 or newer: its default replaces the `X-Forwarded-For` the client sent, which the per-address limits rely on.
Caddy's access log (`log`) records request headers. The apps send the admission key only on the one request that starts a
group, not on every sync, but keep the log off or filtered if you can, and treat it as private.

**On a work or managed PC, avoid this setup.** Without a pin the PC trusts the Windows certificate store, so a TLS-inspecting
proxy or antivirus "HTTPS scanning" there can read the relay's token and admission key (it still cannot read or forge your
sync files). Use the pinned or Tailscale setup (section 10.3) on such a PC. Phones use only the system's built-in authorities.

**The per-address limits need the real client address.** Behind Docker Desktop (Windows, macOS), Docker's userland proxy
(IPv6) or a proxy that doesn't set `X-Forwarded-For`, every client looks like one address and shares one set of limits, so
one stranger can slow everyone down. For a public relay run on Linux with the proxy-mode (host network) lines and Caddy.
A public relay is a target for scanners; the admission key, the token check and the limits stop most abuse, but it is
not hardened like a hosted service. If you can, prefer section 10.3.

### 10.5 On a rented server (VPS)

Any small Linux server works: 1 vCPU, 1 GB RAM and 10 GB of disk are plenty (the files are a few KB each).

1. Set it up as in section 8 (SSH key only, no root login, `ufw`, automatic updates).
2. Firewall: allow **only** SSH, 80 and 443 (`sudo ufw allow OpenSSH`, `sudo ufw allow 80/tcp`, `sudo ufw allow 443/tcp`).
   The relay's own port (8743) stays closed **only when the relay runs with `--proxy`** (it then listens on 127.0.0.1 and
   Caddy reaches it there). In Docker, use the proxy-mode lines of `docker-compose.yml` (host network): the default
   `ports: "8743:8743"` publishes the port around `ufw`, so `ufw` would not keep it closed.
3. Install Docker (or copy the `linux-x64` or `linux-arm64` build and the systemd unit) and run the relay with `--proxy`,
   then set up Caddy as in section 10.4.
4. Or skip Caddy and a DNS name: run it pinned (section 10.2) and open only its port (`sudo ufw allow 8743/tcp`), then use
   `https://<server address>:8743` with the pin and the admission key. The pinned certificate still protects the
   connection; a name and Let's Encrypt only make the address nicer. Without the admission key, strangers could use
   the open port to store files on your server, so never remove that requirement.

### 10.6 Turn it on in AccountabiliBuddy

**PC**

1. Devices → How this PC shares → **Own relay**. Type the address, the pin (empty for a Caddy relay) and the admission
   key, then **Save address**. (A relay you run on this PC with "Run a relay on this PC" fills all three in itself.)
2. Press **Test connection**. It says whether the relay answered and whether the certificate was accepted.
3. Tick **Own relay**. The line under it says "Connected" once the push stream is open.

**Phone and other PCs**

- Devices → **Link a device** on the PC shows a QR code that carries the relay's address, pin and admission key next
  to the link code. Scanning it on the phone joins the group and sets up the relay in one step. (The pin is not secret.
  **The QR is the key to all your data**, so never photograph, screen-share or post it.) The phone first shows what the
  link would change: whether it leaves the group it is in (that asks twice), and the relay's host, with a warning when
  that is a relay on the internet or one the phone doesn't use now. Only tap Join for a link you just made yourself.
- Or enter the address, pin and admission key by hand in the phone's Own relay section.

The PC writes its file about a second after a change, reads other devices' files as soon as the relay says they
changed, and checks again every 5 minutes in case the push stream missed something (every 10 seconds during a session
while the stream is down). It puts its file again every 5 minutes even when nothing changed, so the other devices see
it as up to date. The stream reconnects by itself, waiting 1 second at first and up to 60 seconds after repeated failures.

When this PC leaves the group (or joins another), it deletes its own file from the relay. Other devices' files stay
until those devices leave or 30 days pass.

### 10.7 Keeping the relay patched

(For a relay behind Caddy, update Caddy as well: `sudo apt update && sudo apt upgrade`, or `sudo caddy upgrade`.)

The relay is the one part of AccountabiliBuddy that listens for connections, and the program that does that is .NET's
web server (Kestrel), which gets security fixes (usually on the second Tuesday of a month). **Updating the server's
operating system does not update it**: a self-contained build and a Docker image carry their own copy of .NET, frozen
at the day they were built. So a relay you built once and left running keeps its old runtime until you rebuild it.

| How the relay runs | When to rebuild | How |
|---|---|---|
| On this PC (Devices → Run a relay on this PC) | With every app update | It ships inside the app, so its .NET is only as new as the app you installed: keep the app updated (Updates in About), and update it at once when a .NET security fix for Kestrel is announced. |
| Windows task (`install-windows-task.ps1`) | On every AccountabiliBuddy release, and after each monthly .NET security update | `pwsh scripts/publish-relay.ps1 -Rids win-x64` from an up-to-date source folder, then `pwsh deploy/relay/install-windows-task.ps1 -ExePath .\publish\relay\win-x64\AccountabiliBuddy.Relay.exe -OpenFirewall` (it keeps the data). Install the current .NET 10 SDK first (Windows Update does not update it; `dotnet --version`). |
| Linux (systemd) | Same | `pwsh scripts/publish-relay.ps1 -Rids linux-x64` (or `linux-arm64`), copy the file over `/opt/accountabilibuddy-relay/`, then `sudo systemctl restart accountabilibuddy-relay`. |
| Docker | Same, and at least once a month | In `deploy/relay`: `docker compose build --pull && docker compose up -d`. (`--pull` fetches the newest .NET images; without it Docker may reuse an old one. `restart: unless-stopped` never rebuilds anything.) |

How to check: the relay prints its build and .NET runtime when it starts (`journalctl -u accountabilibuddy-relay`,
`docker compose logs relay`, the PC's relay log) and with `--version` (Docker: `docker compose run --rm relay --version`).
Compare the runtime version with the newest .NET 10 release on dotnet.microsoft.com. The stored files, the certificate
and the admission key (the data folder or volume) stay through a rebuild, so the pin and the key stay.

Put a reminder in your calendar for the second week of every month. A relay on the public internet (section 10.4) that
is months behind is the most likely way for this setup to be attacked; a relay on your home network or a VPN (10.3) is
much less exposed, but patch it too. Keep the relay at least as new as your apps.

After updating the apps from a version before the relay used one group id and token per relay, the devices start a new
group on the relay (the old one is cleaned up after 30 days), and starting a group needs the admission key: enter it on
each device that doesn't get it from a join link.

### 10.8 When the relay doesn't work

| What the app says | What to do |
|---|---|
| "Can't reach the relay right now" | Is the relay running, the address right, the firewall open, the VPN on? Open the address in a browser: a certificate warning is normal for a pinned relay, a "refused" or timeout is not. |
| "The relay's certificate doesn't match the pin" | Nothing was sent. If you deleted `relay.pfx` or moved to a new relay, enter the new pin. If you did not, something may be sitting between the device and the relay: don't go on until you know what. |
| "The relay's certificate isn't trusted" | A relay with its own certificate needs its pin typed in. For a Caddy relay, check the DNS name and that Caddy got its certificate. |
| "This group already belongs to someone else on that relay (403)" | Your group's id and token come from the same link code, so this shouldn't happen for your own group. It means the relay already holds that group id under a different token (for example data left by another build). Delete that group's folder (named by the group id) in the relay's data folder, or wait 30 days, and let the devices write again. |
| "This relay needs its admission key to start a new group (428)" | Enter the admission key the relay printed (Devices → Own relay on the PC, the phone's Own relay section), or join with the link from a device that has it. The key is in `relay-admission.key` and `relay-address-and-pin.txt` in the relay's data folder. |
| "The relay is limiting this PC (429)" | Too many requests, or too many new groups from one address in a day, or the relay already holds its maximum number of groups (`--max-groups`, default 16; groups with no files for 30 days are removed). It tries again by itself. |
| "The relay's disk is full (507)" | Raise `--max-disk-mb` or free space. |
| Changes arrive in minutes, not seconds | The push stream isn't open: some proxies and security software cut long-lived connections. Behind Caddy use the provided Caddyfile; the fallback checks still work (every 10 seconds in a session, else every 5 minutes). |
