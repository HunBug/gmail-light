# Roadmap

Milestone 1 is specified in `MILESTONE_1.md`. Nothing below starts until its RAM measurement passes (D-014). Later milestones are outlines; each gets its own spec when it becomes current.

## Milestone 2: actions

Make the client usable as the only mail tab for triage.

- Mark read on open, mark unread
- Archive, move to inbox
- Star, unstar
- Add and remove labels
- Trash, report spam, and their undo
- Bulk actions on selected rows

Changes with it:

- Scope moves to `gmail.modify`; every account reconnects.
- All mutations are POST with an antiforgery token and a `Sec-Fetch-Site` check (D-011).
- Mutations update the cache optimistically; the next incremental sync confirms them.
- "Undo" is a short-lived inverse action, not a queue.

## Milestone 3: compose

- New message, reply, reply all, forward
- Drafts saved to Gmail so they appear on other devices
- Send-as aliases and signatures from `settings.sendAs`
- Attachment upload
- Undo send as a delayed send held by the backend
- Recipient autocomplete from addresses already seen in the cache, which avoids adding a contacts scope

Open design points for then: plain-text or rich-text editor (a rich editor is the biggest threat to the JavaScript budget), MIME building with MimeKit, and where a delayed send is stored so it survives a backend restart.

## Milestone 4: Gmail-feel extras

- Keyboard shortcuts in the Gmail style (`j`, `k`, `e`, `r`, `/`, ...)
- Saved searches shown as extra sections, in place of multiple inboxes
- Filter list and editing; needs `gmail.settings.basic`
- Per-sender "always show images"
- Desktop notifications for new mail and an unread count in the tab title and favicon

## Milestone 5: local-only features

Things Gmail does not expose and that will not sync with the official apps:

- Snooze
- Scheduled send

Both need a local store that is not a rebuildable cache (D-006) and a backend that is running at the scheduled time. Decide whether they are worth that before designing them.

## Not planned

- Unified inbox across accounts
- Offline mode
- Other providers
- Mobile layout
- Smart Compose, Chat, Meet, confidential mode
