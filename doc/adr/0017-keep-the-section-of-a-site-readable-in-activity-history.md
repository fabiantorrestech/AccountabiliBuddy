# 17. Keep the section of a site readable in activity history

Date: 2026-10-20

## Status

Accepted

## Context

ADR 8 keeps only times, app, site domain, classification and session context readable in activity history, and encrypts window titles and full addresses. History therefore shows time on "reddit.com" as one number, although for the user some subreddits are useful (r/programminghelp) and others are the timewasters they want to see during focus (r/manyBaggers, r/MouseReview); YouTube and GitHub have the same problem with channels and repositories. The full addresses needed to tell them apart are already stored, encrypted. On 2026-10-20 the user chose to store the part that matters readable rather than decrypt addresses whenever History opens, so History stays fast for any period and the breakdown can travel with the readable history shared between paired devices (ADR 15). They chose Reddit subreddits, YouTube channels and GitHub repositories.

## Decision

We will add one readable field to each activity entry, the section of the site, filled in only for a fixed list of sites: a Reddit subreddit (`r/<name>`, read from the address), a YouTube channel (as the browser extension reports it for the page) and a GitHub repository (`owner/repo`, read from the address). Every other site, and pages of these sites outside any section (Reddit's home feed, GitHub's settings), get none. The rest of the address stays encrypted as ADR 8 says. Entries recorded before this change get their section filled in once, on this PC, from their encrypted addresses (Reddit and GitHub; past YouTube entries have no channel recorded). History lists the sections under their site, in Most used and in Top distractions. The section is part of the readable history ADR 15 shares with paired devices. Adding another site to the list needs the user's agreement and a note in CLAUDE.md's activity-history rule.

## Consequences

History answers "which subreddits took my focus time" without decrypting anything, and the user can rename, hide or delete a subreddit's time like any site's. A subreddit, channel or repository name says more about the user than a domain does, and it is readable in the database file and in a backup without secrets, as domains already are; keeping the list fixed and explicit limits how far this goes. The database gains a column (schema 4) with the usual copy before the migration, and an older app version refuses the newer database. Taking it back out means dropping the column and accepting that copies already shared with paired devices or saved in backups keep it.
