> [Version française](../fr/plan.md)

# Plan: the Kazo Design app for Kazo OS

Step 0 of the Kazo OS [Craft app port guide](https://github.com/Kazo-ca/Os/blob/main/doc/en/wasm/dev/craft-app-port-guide.md),
written on 2026-11-14 (`TODO` issue [#1](https://github.com/Kazo-ca/Os-App-Design/issues/1)).
This document sets the goal, what reading DesignCraft showed, the decisions, the questions asked
to Benoit and the work items. Each work item is written so that it can be handed as is to an
implementation thread. The closest reference is Kazo Photo
([plan](https://github.com/Kazo-ca/Os-App-Photo/blob/photo/base/doc/en/plan.md)): same gap
upstream, same path (fork, then app). Kazo Fig
([plan](https://github.com/Kazo-ca/Os-App-Fig/blob/main/doc/en/plan.md)) is the model for tabs
bound by identifier.

## Goal

Kazo Design is **the page-layout app of Kazo OS**: pages and spreads, threaded text frames,
paragraph and character styles, parent pages, tables, PDF export. It wraps the web build of
[DesignCraft](https://github.com/storytold/designcraft) (ArtCraft team, `MIT OR Apache-2.0`, Rust
and egui) and connects it to the files, dialogs and associations of Kazo OS. Kazo Word stays the
word processor; Kazo Design makes brochures, magazines, posters and books.

**What sets this port apart**: like PhotoCraft, DesignCraft has **neither host mode
(`craft-web-host`) nor a command API (`craft-web-api`)**. They have to be written in our fork
[`Kazo-ca/designcraft`](https://github.com/Kazo-ca/designcraft) before the Kazo window can open
or save. Kazo Design is therefore a **modified** build compiled from the fork from its first
version. The work in the fork is smaller than for PhotoCraft: DesignCraft already separates saving the
document from exports. Its `.wasm` is heavier, though (see "Trial").

The common Kazo code (iframe, proxy, file picker, shortcuts, `DocumentSession`, exports) comes
from the `Kazo.Os.AppSdk.Craft` module of Kazo OS
([ADR 0259](https://github.com/Kazo-ca/Os/blob/main/doc/en/wasm/dev/adr/0259-shared-craft-app-host-module.md),
[ADR 0264](https://github.com/Kazo-ca/Os/blob/main/doc/en/wasm/dev/adr/0264-craft-app-exports-reach-the-window.md)),
published in **Kazo OS 3.3.4**, the minimum version of Kazo Design.

## Reading DesignCraft (step 0, 2026-11-14)

Read on `main` of the fork (commit `437c532`, 2026-11-12, version 0.20.1) and compiled from that
commit. The upstream tag `v0.20.1` is commit `33a4678` of the `release` branch, which the fork
does not have yet.

| Point | Finding |
| --- | --- |
| Web build | `apps/designcraft-web` (Trunk, `public_url = "./"`, `wasm-opt -Oz`), eframe on **wgpu**: WebGPU, WebGL 2 fallback (`?webgl`). `?sample` opens the sample magazine. Rust 1.95 at least. Languages are in the code (`crates/ui-egui/src/i18n/`): no `craft-i18n` to provide. |
| Host mode | **Missing.** `apps/designcraft-web/src/web.rs` (450 lines) only knows the browser: Open and Place = browser picker (`rfd`), Save and exports = download. |
| Command API | **Missing** in the web build (`window.designcraft` does not exist), but the base is there: `DesignApp::with_control` takes a queue of `ControlRequest`, the one the desktop TCP server feeds (`docs/control-protocol.md`). Only the `craft-web-api` wiring is missing. Difference from PhotoCraft: no `Dispatch::give_up`; a request the page gave up on would still run (to handle in F1). |
| Canvas | `designcraft_canvas`, hard-coded: one instance per HTML document, so one iframe per window. The page carries `#designcraft_loading`; the graphics error panel (`#designcraft_graphics_error`) is created by the code. |
| Official zip | `designcraft-web-<version>.zip` (`packaging/web/package.sh`): the Trunk output as is, not split. Useless here without host mode; we compile the fork. |
| Saving the document | **Already separate from exports**: when the `Services::download` service is set, Save and Save As both go through `download_document` (`crates/ui-egui/src/menus/export.rs`), which downloads `<title>.designcraft` and then marks the document saved at once (`file.saveAs { path: name }`). On the web, **Save As asks nothing** and does the same as Save. |
| Document format | Always `.designcraft` (native). IDML, PDF, EPUB, etc. are exports, never the document. |
| "Modified" | The document is marked clean **before** the file is written anywhere (the browser confirms nothing). This is the "clean before the write" pitfall (craft-libs #611): in host mode it must wait for `designcraft:saved`. |
| Exports | Through `Services::write(path, bytes)` after `ask_save`, which asks nothing on the web: PDF, PNG of a page, IDML, EPUB (reflowable and fixed), HTML, PowerPoint, XML, text (`.txt`, `.rtf`), booklet PDF, `.ase` swatches, shortcut set. Batch export uses `download`. Save a Copy (`file.saveACopy`) also writes this way. All end as `<a download>`. |
| Open | `file.openBytes { name, base64 }`: `.designcraft`, `.idml`, and `.indd` / `.indt` (converted on open). The document title is the file name without its extension; a document opened from bytes has no path. Files arrive through an inbox (`Services::inbox`, `ImportedFile`) with the request that asked for them. |
| Place | Images (`png jpg jpeg gif webp tif tiff bmp psd svg`), `pdf`, `ai`, text (`txt docx rtf md`), `xlsx`, and also `idml` / `designcraft`. Browser picker; the bytes are embedded in the document. Other file requests: `.ase` swatches, shortcuts (`.json`, `.txt`), styles of another document. |
| Documents | Several documents in **tabs** in one page. `document.list` command: `[{ index, title, dirty, active }]`. The engine has a stable identifier per document (`uid`) and a path (`path`), but `document.list` does not return them: to add (F1). `file.activate { index }`, `file.close { index? }`. |
| Commands | `engine.execute { command, params }`, `ui.menu.invoke`, `engine.commands`, `document.inspect`, `ui.inspect`, `ui.set`, `ui.screenshot`, `ui.render`, `ui.dialog.*`. File: `file.new`, `file.newSample`, `file.openBytes`, `file.save`, `file.saveAs`, `file.saveACopy`, `file.serialize` (the `.designcraft` as base64), `file.close`, `file.activate`, `file.revert`, `file.export*`. Interface: `app.save`, `app.saveDialog`, `app.openDialog`, `app.placeDialog`, `app.export*`. |
| Theme | `ui.set { brightness }` (`window.brightness` command): `dark`, `mediumDark` (default), `mediumLight`, `light`, `highContrast`. |
| Language | `app.language { lang }`: English (`""`), `de fr es ja zh ar pt-br it uk ru cs pl ka hu sv`. |
| Settings | The web build **keeps nothing** between two sessions (see "Trial": no `localStorage`). Preferences, workspace and recent files start from scratch in every window. |
| Fonts | The web build only has its embedded fonts: **Inter, Source Sans 3, Source Serif 4, JetBrains Mono**; with `CRAFT_FONTS_DIR` at build time (as official releases do), BIZ UDPGothic (Japanese) and Noto Sans Georgian are added. No system fonts on the web, no loading on demand (PhotoCraft has one, DesignCraft does not). A document that asks for another font reports it missing. Question Q1. |
| Graphics loss | If the browser loses WebGPU or WebGL, an HTML panel replaces the app and offers "Save a Copy" for each open document (download), then "Reload". |
| Page close | No `beforeunload` warning; the close guard of the Kazo window is the only one. |
| Window size | Desktop: 1440 × 900, minimum 900 × 560. |
| Brand | The ArtCraft name and logos are trademarks (`docs/brand/LICENSE-brand.txt`): a modified build must remove them. The About window draws the ArtCraft mark, the sentence "Part of the ArtCraft suite", a Discord button and the list of the other apps; the Help menu carries five ArtCraft links (`help.discord`, `help.appPage`, `help.github`, `help.issues`, `help.website`); the top bar always shows a "Discord" button; the start screen carries a "Join the ArtCraft community" card with the mark and three links (see "Trial"). |
| Upstream repository rules | `AGENTS.md`: Rust only, no `unwrap`/`panic` outside tests, everything is a command, never break wasm (`cargo xtask wasm`), `cargo xtask ci` before every commit, no CI change in a feature PR, no agent mention, an issue before a PR. |

### Trial (2026-11-14)

Done on the fork (`main`, `437c532`, v0.20.1), in this session's container (4 cores, Linux), with no change to the code.

| Point | Result |
| --- | --- |
| Tools | Stable Rust 1.97.0, `wasm32-unknown-unknown` target, Trunk 0.21.14. No external input (`craft-fonts` not provided). |
| Command | `trunk build --release` in `apps/designcraft-web` (the repository's `release` profile: `opt-level = 3`, thin LTO). |
| Duration | 9 minutes cold: 6 min of `cargo`, then 3 min of `wasm-opt -Oz`. |
| Output | `dist/web`: `index.html`, a 168 KB `.js`, a `.wasm` of **62.3 MB (59.4 MiB)**, 24.2 MB gzipped, a `snippets/` folder. The "13 MB" of `packaging/web/README.md` is no longer true. Sections: 45 MB of code, 17 MB of data. |
| Limits | Under the Kazo OS limits (128 MiB per file; package of 64 MiB compressed, 128 MiB uncompressed), but **above GitHub's 50 MiB warning** per file (refusal at 100 MiB). DesignCraft has no "size" profile like PhotoCraft's `wasm-release`: work item 1 measures an `opt-level = "s"` build (`CARGO_PROFILE_RELEASE_*` variables, without touching the fork) and chooses. |
| Start | Chromium without a graphics card (`?webgl`, WebGL 2 through SwiftShader): the app shows in under a second from a local server. `?sample` opens the sample magazine. |
| Save | Ctrl+S on the sample magazine: download of `Quarterly — Spring Issue.designcraft` (832 KB), with no prompt. |
| Page API | `typeof window.designcraft` is `undefined`. |
| Storage | `localStorage` empty after use: nothing is kept. |
| Console | No error, no failed request. |

Seen on screen: a "Discord" button always in the top bar, and on the start screen a "Join the ArtCraft community" card (ArtCraft mark, links "DesignCraft page", "GitHub", "getartcraft.com", "Join our Discord" button): H9. The 1280 × 820 window is well filled (tools, page, Properties panel).

Screenshots: `design/trials/` in the project files (outside the repository).

## What must be added to DesignCraft (fork)

The contract is that of `craft-web-host` (`storytold/craft-libs`, version "1"), with the
`designcraft:` prefix. The code lives in `apps/designcraft-web`, on the model of
`apps/photocraft-web/src/{api,host,hostmode}.rs` on the `kazo/base` branch of
[`Kazo-ca/photocraft`](https://github.com/Kazo-ca/photocraft/tree/kazo/base/apps/photocraft-web/src)
(itself made on `apps/gridcraft-web`). The points marked ★ need a small opening outside the web
shell (`crates/ui-egui` or `crates/engine`).

| # | Behaviour | Why |
| --- | --- | --- |
| H1 | `designcraft:ready { version: "1", appVersion, file: false }` once the app listens; `designcraft:failed { error }` if it cannot start, **and when rendering is lost** (where the "Graphics connection lost" panel shows). | The Kazo window knows when to send the file and when to show an error. |
| H2 | `designcraft:open { name, bytes }` opens the file like File › Open, in a new tab, refused above a size limit before it is copied. ★ A `.designcraft` given by the host **keeps its name as its path**; an `.idml`, `.indd` or `.indt` has none (it is converted, its first save creates a `.designcraft`). | Save rewrites the original file only when it really is that file. |
| H3 | ★ In host mode, a save of the document goes out as `designcraft:save { name, bytes, saveAs, id }` instead of a download. `saveAs` is false when the document is rewritten under its path, true otherwise: new document, document that came from an IDML or INDD, and **Save As** (which must be distinct from Save again). | The host knows whether it rewrites the bound file or asks where to save. |
| H4 | ★ The document is marked saved only when `designcraft:saved { id }` arrives; without an answer it stays modified. | Fixes "clean before the write": a dialog cancelled on the Kazo side loses nothing. |
| H5 | **Exports, copies and side files** (`Services::write`, batch export, "Save a Copy" of the recovery panel) stay downloads. | The Kazo module intercepts them and hands them to the window as copies (ADR 0264). |
| H6 | `designcraft:dirty { dirty }` when one of the open documents has changes, or none has any more. | Window title. |
| H7 | `window.designcraft` (`craft-web-api`: `request`, `execute`, `commands`, `docInspect`, `uiInspect`, `screenshot`) on the control queue, installed with `?control`. ★ A request the page gave up on does not run (equivalent of PhotoCraft's `Dispatch::give_up`). ★ `document.list` also returns `uid` and `path`. | Theme, language, document list bound by stable identifier, close guard. |
| H8 | Outside host mode, nothing changes. | The patch can still be proposed upstream. |
| H9 | In host mode: the About window does not draw the ArtCraft mark and replaces its sentence and links by the permitted text ("based on DesignCraft by the ArtCraft team"); the five ArtCraft links of the Help menu, "Check for Updates", the "Discord" button of the top bar and the "Join the ArtCraft community" card of the start screen are not shown. | Modified build. |

Tests asked in the fork: unit tests of the pure rules (`saveAs`, kept path, size limit, waiting
for `saved`), `cargo xtask ci` (fmt, clippy `-D warnings`, tests, assets, layers, wasm), a Trunk
build, and a Playwright journey on a trial page that plays the host (`open`, `save`, `saved`,
`dirty`, `failed`, `window.designcraft.request`).

## Decisions

Decisions 2, 6 and 7 take Benoit's answers for Kazo Photo (Q1 to Q3 = A, 2026-11-13); point 2 is
also the instruction given to this plan.

| # | Subject | Decision |
| --- | --- | --- |
| 1 | Name and identity | "**Kazo Design**". Id `ca.kazo.design`, publisher `pub.kazo`, assembly `Kazo.Design`, category `graphics`, several windows, window of 1280 × 820, minimum content 900 × 560, `canChangeScreen: false` (ADR 0263). |
| 2 | Where host mode lives | In our fork now, proposed upstream in parallel (issue drafts in English, published by Benoit). |
| 3 | Common code | `Kazo.Os.AppSdk.Craft` module, **Kazo OS 3.3.4** at least (exports as copies, ADR 0264). `external/KazoOs` submodule on the `v3.3.4` tag, nuget.org packages in CI, from work item 1. |
| 4 | Origin of the build | Always compiled from the fork: branch `kazo/v0.20.1` (= upstream tag `v0.20.1`, commit `33a4678`), integration branch `kazo/base` created from it. `external/designcraft` submodule on `kazo/base`. Trunk output copied into `wwwroot/designcraft/` with `designcraft.json` (version, commit, names and SHA-256), in the repository: the Store does not compile Rust. |
| 5 | Iframe page | `wwwroot/host/index.html`: `designcraft_canvas` canvas with `data-host="page"`, `data-craft-app="designcraft"`, `data-craft-build="../designcraft/designcraft.json"`, plus `#designcraft_loading`. `control` parameter. |
| 6 | Tabs | We keep DesignCraft's tabs: a window holds several documents, each tab bound to its file by the document's `uid` (H7). A double-click in the file explorer opens a new window. A file already open in the window brings its tab back. |
| 7 | Associations | Default app (`open`, `edit`) for `.designcraft`, `.idml`, `.indd`, `.indt`: nothing else opens them in Kazo OS. No association for the formats that are placed (images, `.docx`, `.pdf`…): they are placed into a document, they are not opened. |
| 8 | Saving | `Saving`: the module's rule (`DocumentSession.Decide(name, saveAs)`); the window returns `true` only when the file is written, which triggers `saved` (H4). A document that came from an `.idml` or `.indd` is saved as `.designcraft` through the OS dialog, in the folder of the original file, which is never rewritten. `Exporting`: always a copy (`DecideCopy`). |
| 9 | Close guard | Read from `document.list` (`uid`, `title`, `dirty`); the window activates each modified document (`file.activate`), runs `app.save` and waits for its writes to end. |
| 10 | Theme and language | `ui.set { brightness }`: `dark` for the OS dark theme, `light` for the light one, at every change. Language: `app.language` at startup (`CraftLanguage.Pick` on the 15 codes; English otherwise). As DesignCraft keeps nothing, the window sets them at every start. |
| 11 | Settings | Nothing to keep per user in v1: the web build writes no storage. Limit written in the listing (workspace and preferences start from defaults). If upstream adds storage, the module's `WebStorageFile` will take it. |
| 12 | Shortcuts | `ReservedKeys.Os` **without** `Ctrl+N` (New document). Conflicts between the OS chords and DesignCraft's (`Ctrl+F6`, `Alt+Shift+F11`, `Shift+F9`…) are listed in work item 2 and noted in the listing. |
| 13 | Large files | The Kazo OS limit (128 MiB per file, ADR 0245) applies. Work item 2 measures opening and saving a document with 30 to 50 MB of embedded images. |
| 14 | Upstream track | Issue drafts in English, no agent mention, in `/mnt/project-files/design/upstream/`; Benoit publishes them. The fork branches are written so they can become upstream PRs. |
| 15 | Kazo OS ADR | None planned: the module has what is needed. If a work item needs one, next free number **0271** on 2026-11-14 (check again on `main`, the open PRs and every `*/base` branch when writing it; Kazo Deck and Kazo Vector move in parallel). |

## Questions asked to Benoit

Asked in the thread on 2026-11-14. **Benoit answered A on 2026-11-14**: v1 ships with the fonts of the official build, the limit is written in the listing, and the fonts-on-demand work item (F3) comes after the release.

**Q1. Document fonts.** The DesignCraft web build only has four families (Inter, Source Sans 3,
Source Serif 4, JetBrains Mono) and does not read system fonts. For a page-layout app this is the
most visible limit: an `.idml` from elsewhere will almost always show missing fonts.

- **A (recommended)**: v1 with the fonts of the official build (the four families, plus the
  Japanese and Georgian of `craft-fonts`), limit written in the listing; then a fork work item
  **after** the release (F3) that adds loading fonts on demand, on the model of PhotoCraft, with
  the Latin fonts of `craft-fonts` Kazo Photo already uses (15 families, 19 MB). Kazo Design
  ships sooner, and F3 can be proposed upstream.
- B: F3 before the release. v1 has about twenty families at once, but it waits for one more Rust
  work item in DesignCraft's font engine, the least certain of the plan.
- C: embed the Latin fonts of `craft-fonts` in the `.wasm` at build time. Simple (one condition
  to change in `crates/fonts/build.rs`), but the `.wasm` grows by about 19 MB, loaded at every
  start, and this patch cannot be proposed upstream.
- D: four families only, nothing planned after.

## Work items

Two repositories: the fork `Kazo-ca/designcraft` (Rust) and `Kazo-ca/Os-App-Design` (C#). In
each, one integration branch, one branch and one PR per work item; one implementation thread per
work item. Benoit merges `design/base` into `main`; the fork's `main` stays the copy of upstream.

| No. | Repository, branch (to) | Content | Depends on |
| --- | --- | --- | --- |
| 0 | Os-App-Design, `design/1-plan` (`design/base`) | this plan (fr and en) | — |
| F1 | designcraft, `kazo/1-web-api` (`kazo/base`) | H7: `window.designcraft`, `document.list` with `uid` and `path` | plan merged |
| F2 | designcraft, `kazo/2-web-host` (`kazo/base`) | H1 to H6, H8, H9: host mode | F1 (same files) |
| 1 | Os-App-Design, `design/2-solution` (`design/base`) | solution, manifest, build from the fork, iframe page, window that starts | plan merged |
| 2 | Os-App-Design, `design/3-window` (`design/base`) | open, save, tabs, exports, close guard, picker, shortcuts, theme, language | 1 and F2 |
| 3 | Os-App-Design, `design/4-store` (`design/base`) | listing, README, licences, measures, tag, catalog, `features.md` | 2 |
| F3 | designcraft, `kazo/3-served-fonts` (`kazo/base`), then Os-App-Design `design/5-fonts` | fonts on demand (Q1 = A: after the release) | 3 |

**Order**: F1 then F2 in one thread; work item 1 in a second thread, at the same time; then 2;
then 3. Kazo OS 3.3.4 is already published: nothing waits for an OS release.

### F1. `window.designcraft` (fork)

- Create `kazo/v0.20.1` on upstream commit `33a467872268c98536231491865b4011b0275b17`
  (`git fetch https://github.com/storytold/designcraft refs/tags/v0.20.1`), then `kazo/base` from
  that branch. Push both.
- `apps/designcraft-web/src/api.rs` on the model of `apps/photocraft-web/src/api.rs` of the
  PhotoCraft fork: a `ControlRequest` queue given to the app through `DesignApp::with_control`,
  `craft_web_api::page::PageApi::new("designcraft", …).install()`, installed with `?control`.
  `craft-web-api` pinned to a revision of `storytold/craft-libs`, like the other `craft-*`.
- ★ A request the page gives up on does not run: DesignCraft has no `Dispatch::give_up`, it needs
  the smallest opening in `crates/ui-egui/src/control.rs` or `app/control.rs` (read how PhotoCraft
  does it).
- ★ `document.list` (`crates/engine/src/cmd/inspect.rs`): add `uid` and `path` to each document,
  with a test.
- `ui.screenshot` without a path must return the PNG to the page (check, adapt like PhotoCraft's
  `adapt`).
- **Done when**: in a page served by `trunk serve`,
  `await window.designcraft.request("engine.execute", { command: "document.list" })` answers with
  `uid`, `ui.set { brightness: "light" }` changes the theme, `app.language { lang: "fr" }` changes
  the language, and `cargo xtask ci` passes.

### F2. Host mode (fork)

- `apps/designcraft-web/src/host.rs` and `hostmode.rs` on the model of the PhotoCraft fork:
  `Host::connect("designcraft", canvas)`, listening to `open` and `saved`, `post_ready`,
  `post_save`, `post_dirty`, `post_failed`.
- H2: the host's file enters through the inbox (`ImportedFile` with `import_request("open")`),
  like a picked file. ★ A `.designcraft` keeps its name as its path.
- H3, H4 ★: today `download_document` downloads and then marks saved. In host mode, a separate
  service (shape to choose after reading `menus/export.rs`, `menus/run.rs` (`save_then`,
  `app.save`, `app.saveDialog`) and `services.rs`) hands the bytes to the host with `saveAs`, and
  the document is marked saved (with its path) only at `saved { id }`. `save_then` (save then
  close, then export…) must run what follows only after the confirmation. Save As sends
  `saveAs: true`.
- H5: change nothing in `Services::write` or in batch export.
- H1: `post_failed` from `graphics_failed`; H9; a line in the web build doc
  (`packaging/web/README.md`) that describes host mode.
- **Done when**: the trial page that plays the host opens a `.designcraft` and an `.idml`,
  receives `save` with the right `saveAs` in the four cases (rewrite of a `.designcraft`, document
  from an `.idml`, new document, Save As), the document is clean again only after `saved`, Export
  PDF still downloads, the same page without `data-host` behaves as before, and the thread gives
  the `kazo/base` commit to pin.

**Done on 2026-11-14** (fork `Kazo-ca/designcraft`, PRs [#1](https://github.com/Kazo-ca/designcraft/pull/1) and [#2](https://github.com/Kazo-ca/designcraft/pull/2), merged into `kazo/base`):

| Item | Result |
| --- | --- |
| Commit to pin | **`kazo/base` = `9db88589d703a97c707d37bbd95cdfc348889a01`** (F1 `33bed97`, F2 `6e297b7`, on the upstream tag `33a4678`). |
| F1 | `?control` installs `window.designcraft` (`craft-web-api`, craft-libs `6e03f65`). `ControlRequest` carries a `Dispatch`: a request the page gave up on is not run (reply `timeout: not run`). `ui.screenshot { data: true }` returns the PNG as base64. `document.list` gives `uid` and `path`; `file.openBytes` takes an optional `path`, kept for a `.designcraft` only. |
| F2 | With a host (`craft-web-host`): `ready` once the app exists, `failed` when graphics or opening fail; `open` keeps the name; Save hands the bytes to the host with an `id`, and the document is clean again only at `saved { id }`, at the revision handed over (edits made after stay unsaved). `saveAs: true` for Save As, a new document and an imported document (IDML, INDD). `save_then` waits for the confirmation. Exports download as before. H9: no ArtCraft mark, Discord button, community card, external Help links or update check; About says "Based on DesignCraft by the ArtCraft team.". Without a host, nothing changes. |
| Browser trial | `trunk build --release` build, headless Chromium, trial host page: F1 (`document.list` with `uid`, light theme, French, screenshot), H1, H2, H3 (`saveAs: false`), H4 (dirty until `saved`), H6 (`dirty` false, true, false), Save As, `.idml` then `Imported.designcraft`, new document, Ctrl+S to the host, Export PDF downloads, save then close waits, no `.designcraft` download in host mode; the same page without a host downloads as before. Screenshots in the project files (`design/trials/fork-f2/`). |
| Tests | 12 Rust host-mode tests, F1 and `hostmode` tests. `cargo xtask` fmt, clippy, layers, assets, wasm: pass (Rust 1.97.0). Workspace tests pass except 3 `ui-egui` tests that fail the same way on `kazo/base` in this environment (CJK glyph, wgpu under kittest). |
| GitHub CI | `gate` red on both PRs: the runner took Rust 1.100.0 (2026-11-09), whose clippy reports `needless_range_loop` in `crates/engine/src/cmd/paths.rs:83`, upstream code from the tag that the PRs do not touch. `kazo/base` fails the same way. To fix upstream or at the fork's next move to an upstream release. The other checks (lint, nix, SonarCloud, Windows installation policy) are green. |

### 1. Solution and build (`design/2-solution`)

- Renamed solution (`Kazo.Design`, `Kazo.Design.Os`, `Kazo.Design.Tests`), `[AppManifest]` of
  decision 1, capabilities of Kazo Photo, associations of decision 7.
- `external/KazoOs` submodule on `v3.3.4`, `external/designcraft` submodule on `kazo/base` (on
  `kazo/v0.20.1` while F2 was not merged).
- `tools/build-designcraft.sh` (stable Rust, `wasm32-unknown-unknown` target, Trunk 0.21.14,
  `trunk build --release` in `apps/designcraft-web`, `CRAFT_FONTS_DIR` on `craft-fonts` at the
  revision of DesignCraft's `release.yml` for Japanese and Georgian), `designcraft.json`,
  `build-designcraft.yml` workflow, xUnit test "the commit of `designcraft.json` is the
  submodule's". Model: `Os-App-Photo` (`tools/build-photocraft.sh`).
- Iframe page (decision 5) and window on `CraftFrame` and `FrameStartup`: loading, app, error,
  "Retry", theme and language at `ready`.
- **Done when**: the app starts in WebGL 2 (and WebGPU where available), two windows run
  together, the `.kazoapp` package installs in published Kazo OS 3.3.4; package size and memory
  per window noted here.

**Done on 2026-11-14** (branch `design/2-solution`):

| Point | Result |
| --- | --- |
| Solution | `Kazo.Design.slnx`: `src/Kazo.Design`, `src/Kazo.Design.Os`, `tests/Kazo.Design.Tests`. `[AppManifest]` `DesignApp` (decision 1), Kazo Photo's capabilities, four default formats (`.designcraft`, `.idml`, `.indd`, `.indt`, decision 7). |
| Submodules | `external/KazoOs` on `v3.3.4`; `external/designcraft` first on `kazo/v0.20.1` (`33a4678`), then on `kazo/base` (`9db8858`, F1 and F2) on 2026-11-14, rebuilt: 43.3 MB `.wasm`. |
| Build | `tools/build-designcraft.sh`: Trunk 0.21.14, Rust 1.97.0, `craft-fonts` at `b076417` (BIZ UDPGothic and Noto Sans Georgian), size profile through `CARGO_PROFILE_RELEASE_OPT_LEVEL=s`, `CARGO_PROFILE_RELEASE_LTO=fat`, `CARGO_PROFILE_RELEASE_CODEGEN_UNITS=1`, without changing the fork. 12.5 min cold (4 cores): 11 min of `cargo`, then `wasm-opt -Oz`. `tools/vendor-designcraft.sh` writes `designcraft.json` (provenance, size and SHA-256 of each file). "Build DesignCraft" workflow on Kazo Photo's model. |
| Size | `.wasm` of **43.2 MB (41.2 MiB)**, 19.5 MB gzip -9, against 62.3 MB at `opt-level = 3` (step 0): under GitHub's 50 MiB warning. The 4.4 MB over the measure without fonts (38.8 MB) are the two `craft-fonts` faces. A test refuses a `.wasm` over 50 MiB. |
| Package | `ca.kazo.design-0.1.0.kazoapp`: 19.6 MB. |
| Smoothness | Page alone, Chromium without a GPU (WebGL 2 through SwiftShader, 1280 × 800), sample magazine, wheel scrolling and zooming: two alternating runs per build. Size profile: starts in 0.75 to 0.84 s, frame time median 200 ms, p90 250 to 267 ms, p99 450 ms. `opt-level = 3` build (66.3 MB) on the same page: starts in 0.87 to 1.03 s, median 200 ms, p90 250 to 267 ms, p99 417 ms. **No measurable smoothness difference**; rendering is the same. These times measure SwiftShader's software rendering, not a GPU. |
| Published Kazo OS 3.3.4 | `kazopkg install` of the package in the published site (`kazoos-wasm-3.3.4.zip`): installed, two windows open together. DesignCraft runs after 6.1 s in the first (including the `.wasm` download), 0.8 s in the second. WebGL 2 used; WebGPU is not available in this GPU-less Chromium, so not checked. |
| Memory | Tab renderer process (RSS): 1,042 MiB for Kazo OS alone, 1,491 MiB with one window (+449), 1,499 MiB with two. Rough measure: the second window reuses the compiled module and does not touch all its memory yet. |
| Window | `DesignWindow` on `CraftFrame` and `FrameStartup`: loading, error, "Retry", 60 s timeout, theme (`ui.set brightness`) and language (`app.language`) at `ready`. Iframe page `wwwroot/host/index.html` with no inline script or style. With F2's host mode (`data-host="page"`), DesignCraft sends `ready` and the window shows it. |
| Tests | `dotnet test`: 54 passed (manifest, formats, page, build provenance: submodule commit, `craft-fonts`, profile, licences). E2E (`npm run test:e2e`, Release): 4 passed on the `kazo/base` build, including "window shows DesignCraft once ready". |

### 2. Window (`design/3-window`)

- Opening: file asked at launch, File › Open through the OS dialog, decision 6.
- Saving: decision 8, the four cases of F2 end to end in Kazo OS; a cancelled dialog or a refused
  write leaves the document modified.
- Tabs bound by `uid`, window title on the active tab, close guard (decision 9).
- Exports as copies (`Exporting`): PDF, IDML, EPUB, PNG, etc.; no browser download leaves the
  app. File picker (`PickRequested`) for Place, swatches, styles and shortcuts. Shortcuts
  (decision 12), theme and language followed live (decision 10).
- Measure of decision 13.
- **Done when**: no case loses changes unless the user chose it, no browser dialog or download
  leaves the app, and the OS shortcuts work with the cursor in the page.

**Done on 2026-11-14** (branch `design/3-window`):

| Item | Result |
| --- | --- |
| Fork | Two web build fixes, merged into `kazo/base`: [#3](https://github.com/Kazo-ca/designcraft/pull/3) (Load Styles asked for the same files as File › Open; now only `.designcraft` and `.idml`, which lets the window tell the two pickers apart) and [#4](https://github.com/Kazo-ca/designcraft/pull/4) (Save a Copy wrote nothing on the web: the copy now goes through the platform writer, so through `Exporting`). Submodule `external/designcraft` on the result, rebuilt: 43.3 MB `.wasm`. Upstream drafts completed in `/mnt/project-files/design/upstream/`. |
| Opening | File asked at launch (double-click, `xdg-open`) and DesignCraft's File › Open (and the start screen's "Open…") through the OS open dialog, several files at once, in the folder of the document in front. DesignCraft hears "cancelled", then the window opens each file itself (`file.openBytes`) and binds it to its new tab. A file already open brings its tab back (`file.activate`). |
| Tabs bound by `uid` | `DesignDocuments` (decision 6): each tab's file by the `uid` of `document.list`, not by its name. Two `Brochure.designcraft` from two folders keep their own; Save As binds the chosen file even though DesignCraft keeps the suggested name; a closed tab loses its binding. The window title follows the active tab (`*name — Kazo Design` when modified). |
| Saving | The four cases of F2: new document (OS dialog), bound document (rewritten with no dialog), Save As (dialog, new binding), document from an `.idml` or `.indd` (dialog for a `.designcraft` in the original's folder, which is never rewritten, decision 8). `saved` is sent only once the file is written: a cancelled dialog or a refused write leaves the document modified and says so in the window. |
| Exports | `Exporting`: always a copy, asked in the document's folder (Save a Copy, PDF, IDML, and the other exports that go through the writer). The document keeps its file and its modified state; a "Copy saved" notice. No browser download. |
| Close guard | Decision 9: the window reads `document.list` again; with modified tabs left, the OS box "Save the changes?" (Yes, No, Cancel). Yes activates each modified tab, runs `app.save` and waits for each write (2 min at most); a cancelled save keeps the window open. |
| Pickers | `PickRequested`: File › Open recognised by its extension list (`DesignFormats.IsOpenPicker`); Place, `.ase` swatches, styles, shortcuts get the bytes of the file chosen in the OS dialog, filtered on what DesignCraft accepts, then "All files". |
| Shortcuts | Decision 12: no DesignCraft menu shortcut is a chord the OS keeps (`Alt+Tab`, `Alt+F4`, `Alt+Q`, `Alt+Space`, `Alt+F3`, `Alt+F7` to `Alt+F11`, `Super+arrows`, `Alt+Shift+Left/Right`, `Super+Shift+Left/Right`, `Ctrl+Alt+Shift+Left/Right`). Close but distinct: `Alt+Shift+F11`, `Cmd+F11`, `Cmd+F6`, `Shift+F9`. `Ctrl+N` stays DesignCraft's. Checked end to end: `Alt+Q` pressed with the cursor in the page closes the window through the close guard. |
| Theme and language | Theme followed live (`SchemeChanged` → `ui.set brightness`); language set at each start (`app.language`, decision 10). |
| Large document (decision 13) | `tests/measure/large-document.mjs`: 4 PNG images of uncompressed noise, 40 MB embedded, a 40.0 MB document (40,026,626 bytes). Chromium with no graphics card, WebGL 2 through SwiftShader. Placing: 3.6 to 3.8 s (two runs). `file.serialize` alone: 1.5 s. First save (from the dialog's Save button to the title without its star): 27.5 to 27.8 s; save in place: 28.3 s. Opening from the OS: DesignCraft ready in 8.5 s, document shown in 28.1 to 28.6 s. The ~26 s outside DesignCraft are carrying the bytes and `KazoFile` (see [Os#520](https://github.com/Kazo-ca/Os/issues/520), `KazoFile` slow at 50 MB): to be written in the listing; nothing is lost. |
| Tests | `dotnet test`: 70 passed (15 of them on `DesignDocuments` and recognising the open picker). E2E `tests/e2e/open-save.e2e.mjs`: 7 passed (new document, Save As, copy and PDF, close guard Cancel / Yes + Cancel / Yes + name, Open through the OS dialog and the tab coming back, converted IDML never rewritten, Load Styles with no new tab, `Alt+Q`). These tests start Chromium without WebGPU: in the test Chromium, a second DesignCraft on software WebGPU (SwiftShader) loses its device ("Graphics connection lost"); two windows work on WebGL 2 and in a real browser (work item 1). |

### Update to DesignCraft 0.21.1 (`design/update-designcraft-0.21.1`, 2026-11-23)

Benoit's choice (1B 2A 3A): move to the latest upstream version, the official tag when one
exists. DesignCraft published the tag `v0.21.1` (commit `7470fea`, 2026-11-23).

| Point | Result |
| --- | --- |
| Fork | Branch `kazo/v0.21.1` on the upstream tag `v0.21.1`, and a new integration branch `kazo/base-v0.21.1` created from it, with our four patches rebased (F1, F2, #3, #4). `kazo/base` and `kazo/v0.20.1` stay as they are. Conflicts resolved: one `craft-libs` revision (`33be356`, upstream's) for `craft-web-api` and `craft-web-host`; `file.openBytes` keeps host mode's `path` and upstream's placed-file links; `file.saveACopy` (moved upstream to `save.rs`, with `embedLinks`) still returns its bytes on the web. A fifth patch: `craft-libs` `33be356` adds the `attach-pdf` message, which DesignCraft refuses and tells the page (`failed`). |
| Submodule | `external/designcraft` on `kazo/base-v0.21.1` (`.gitmodules` follows that branch), rebuilt. |
| New for users | Placed files are linked instead of embedded (1024 px proxy, status OK, Modified or Missing, Update, Relink, Embed; native format 5), placing several images and contact sheets, Duplicate Layer, object styles (panel, nested paragraph styles, corners), cell diagonal lines, Go to Row, Paragraph Rules, type shortcuts, scrolling document tabs, many speed-ups of image rendering and composition. Languages: the same 16. |
| Tests | Fork: `cargo check` and `cargo test` of `designcraft-web` (15) and of the engine filtered on `file` (156), Rust 1.97.0; wasm `cargo clippy` stops on 5 warnings in upstream code (`book.rs`, `MAX_PREVIEW_PIXELS`, `package.rs`, `recovery.rs`), not in our patches. App: 48.8 MB `.wasm` (43.3 MB for 0.20.1), `dotnet test` 70 passed, E2E (`npm run test:e2e`) 11 passed. |

### 3. Store (`design/4-store`)

- Icon without ArtCraft mark, listing in fr and en ("based on DesignCraft by the ArtCraft team",
  known limits: fonts, settings not kept, 128 MiB per file, shortcuts kept by the OS),
  screenshots, README rebuilt from the listing (`store:readme`, "Try it" with
  `?autolaunch=ca.kazo.design`), `THIRD-PARTY-NOTICES`.
- Journey in published Kazo OS 3.3.4 (`tests/installed`), "Tag a release" workflow, PR to the
  `Os-AppMarketPlace` catalog (signing only there), `features.md` row in `Kazo-ca/Os` (fr and en).
- **Done when**: Kazo Design installs from the Store, opens a `.designcraft` by double-click,
  saves it, and exports a PDF into a folder of the user.

### F3. Fonts on demand (after the release, Q1 = A)

- Fork: load a family at its first use from `fonts/manifest.txt` relative to the page, on the
  model of PhotoCraft; the font list shows served families before they are loaded.
- App: `tools/vendor-fonts.sh` of Kazo Photo (Latin fonts of `craft-fonts`, OFL), licences in
  `THIRD-PARTY-NOTICES`.

## Upstream track

Drafts in English, published by Benoit, in `/mnt/project-files/design/upstream/`: DesignCraft,
host mode of the web build (`craft-web-host`); DesignCraft, `window.designcraft`
(`craft-web-api`) and `document.list` with `uid`. Not verified: issues already open upstream
(`storytold` issues are out of this session's reach); Benoit looks before publishing.

## Progress

- [x] Step 0: reading DesignCraft 0.20.1, building and trying the web build.
- [x] Plan in fr and en.
- [x] Benoit's answer to Q1: A (2026-11-14).
- [x] F1, F2 (fork): merged into `kazo/base` on 2026-11-14 (`9db8858`).
- [x] Work item 1: solution, build, window that starts (2026-11-14, on `kazo/base` after F2).
- [x] Work item 2: window, open and save, exports (2026-11-14, fork #3 and #4).
- [x] Update to DesignCraft 0.21.1 (2026-11-23, fork `kazo/base-v0.21.1`).
- [ ] Work item 3: Store.
- [ ] F3: fonts on demand.
