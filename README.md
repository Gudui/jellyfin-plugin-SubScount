# SubScout — Local subtitles that just show up

> **Inspired by and built on**: [azam/jellyfin-plugin-localsubs](https://github.com/azam/jellyfin-plugin-localsubs/).
> SubScout extends it with deeper folder matching, sensible defaults, multiple filename patterns, and automatic runs after scans/changes.

## What it does (the “so what”)

* Finds existing subtitle files around your media (including in `Subs/` folders).
* Copies/moves them next to the video using smart, common naming patterns.
* Runs automatically after library scans and file changes, or on demand.

## Works out-of-the-box

No setup required. SubScout ships with robust defaults (language synonyms, common templates, safe copy/move behavior). Install → restart Jellyfin → it starts placing subs.

## Quick install (from a release)

1. **Download** the latest release asset (ZIP or `SubScout.Plugin.dll`) from this repo’s **Releases** page.
2. **Copy** to your server:

   * Create a folder: `/config/plugins/SubScout_1.0.0/`
   * Place the DLL (or unzip the release) inside that folder.
3. **Restart Jellyfin**.
4. Verify under **Dashboard → Plugins → SubScout** (optional: use the **Test** button).

> Docker path hint: the plugin path is inside your container at `/config/plugins/…`.

## Optional tuning (if you want it)

* **Dashboard → Plugins → SubScout**
  Adjust:

  * Templates (filename patterns like `%fn%.%l%.%fe%`)
  * Subtitle extensions
  * Language synonyms (e.g., `en|eng|english`)
  * Copy vs. Move, overwrite rules

Defaults already cover the common cases.

## When it runs

* **Automatically** after library scans and file changes.
* **On demand**: Dashboard → **Scheduled Tasks** → “SubScout: Local subtitle sweep”.
* **From the plugin page**: click **Test** to try your current settings.

## Why SubScout vs the original?

* Deeper, recursive matching out of the box
* Rich, ready-to-go patterns and language groups
* Multiple file strategies (copy/move/overwrite)
* Hooks that actually fire on Jellyfin 10.10.x (scan + change)

Again, big thanks to **azam/jellyfin-plugin-localsubs** for the original idea and groundwork.
