// End-to-end runs of opening, saving, exporting and closing (doc/en/plan.md, work item 2,
// decisions 6 to 9), inside the Kazo OS host: `npm run test:e2e` after
// `dotnet build Kazo.Design.slnx -c Debug`.
//
// DesignCraft is driven through its page API (window.designcraft in the iframe) to create a
// document and run its menu items, as its menus and shortcuts do; everything else goes through the
// OS: its save and open dialogs, its message box, the window's title bar and close button, and the
// Terminal (`xdg-open` starts the app associated with the file type). DesignCraft starts with
// --webgl, as in design.e2e.mjs.
import { after, afterEach, before, beforeEach, test } from 'node:test';
import assert from 'node:assert/strict';
import { runInTerminal, startHost } from './host.mjs';
import {
    HOME, STEP_TIMEOUT_MS, answerSaveDialog, bootPage, designWindows, documentsOf, expectTitle, frameOf, launchBrowser, menu, modify,
    newModifiedDocument, request, shot, waitForDocument, waitForWindow, windowById,
} from './common.mjs';

const SAVE_TITLE = 'Save the document';
const COPY_TITLE = 'Save a copy';
const OPEN_TITLE = 'Open documents';
const PICK_TITLE = 'Choose a file';

let host;
let browser;

// One browser per test, without WebGPU (see launchBrowser), so that each test starts from a
// clean GPU process.
before(async () => {
    host = await startHost();
});

beforeEach(async () => {
    browser = await launchBrowser({ webgpu: false });
});

afterEach(async () => {
    await browser?.close();
});

after(() => {
    host?.stop();
});

/** Opens Kazo Design on WebGL 2 from the Terminal; returns its window id and DesignCraft's frame. */
async function launch(page, known = [], argument = '') {
    await runInTerminal(page, `launch ca.kazo.design --webgl ${argument}`.trim());
    const id = await waitForWindow(page, known.length + 1, known);
    return { id, frame: await frameOf(page, id) };
}

/** Waits for the Terminal's output to show `text`. */
async function expectInTerminal(page, text) {
    await page.locator('.xterm-rows').last().getByText(text, { exact: false }).first().waitFor({ timeout: STEP_TIMEOUT_MS });
}

/** The text the Terminal shows after `ls -l <folder>`. */
async function listing(page, folder, expected) {
    await runInTerminal(page, `ls -l ${folder}`);
    await expectInTerminal(page, expected);
    return page.locator('.xterm-rows').last().innerText();
}

/** A new document saved as `fileName` in the home folder, through File › Save and the OS dialog. */
async function saveNewDocument(page, id, frame, title, fileName) {
    await newModifiedDocument(frame, title);
    await menu(frame, 'app.save');
    await answerSaveDialog(page, SAVE_TITLE, fileName);
    await expectTitle(page, id, `${fileName} — Kazo Design`);
    await waitForDocument(frame, doc => doc.active && !doc.dirty, 'clean after the save');
}

/** Picks `fileName` (in the folder the dialog opens in) in the OS Open dialog titled `title`. */
async function pickInDialog(page, title, fileName) {
    const dialog = page.locator('section.file-dialog[role="dialog"]').last();
    await dialog.getByRole('heading', { name: title }).waitFor({ timeout: STEP_TIMEOUT_MS });
    await dialog.getByText(fileName, { exact: true }).dblclick().catch(async error => {
        await shot(page, `pick-${fileName}`);
        throw error;
    });
    await dialog.waitFor({ state: 'detached', timeout: STEP_TIMEOUT_MS });
}

test('a new document is saved where the user says, saved again in place, and reopens from the OS', { timeout: 400_000 }, async () => {
    const { page, errors } = await bootPage(browser, host.baseUrl);
    const { id, frame } = await launch(page);

    await newModifiedDocument(frame, 'Flyer');
    await expectTitle(page, id, '*Flyer — Kazo Design');

    // The first save of a new document asks where to write it.
    await menu(frame, 'app.save');
    await answerSaveDialog(page, SAVE_TITLE, 'flyer.designcraft');
    await expectTitle(page, id, 'flyer.designcraft — Kazo Design');
    await waitForDocument(frame, doc => doc.active && !doc.dirty, 'clean after the save');

    // The next save writes the same file, with no dialog.
    await modify(frame);
    await expectTitle(page, id, '*flyer.designcraft — Kazo Design');
    await menu(frame, 'app.save');
    await expectTitle(page, id, 'flyer.designcraft — Kazo Design');
    assert.equal(await page.locator('section.file-dialog').count(), 0);

    // `xdg-open` starts the app associated with .designcraft: a second window, on the file.
    await runInTerminal(page, `xdg-open ${HOME}/flyer.designcraft`);
    const second = await waitForWindow(page, 2, [id]);
    await expectTitle(page, second, 'flyer.designcraft — Kazo Design');
    await waitForDocument(await frameOf(page, second), doc => doc.path === 'flyer.designcraft' && !doc.dirty, 'flyer.designcraft');

    assert.deepEqual(errors, []);
    await page.close();
});

test('Save As binds the document to a new file; Save a Copy and Export PDF are copies', { timeout: 400_000 }, async () => {
    const { page, errors } = await bootPage(browser, host.baseUrl);
    const { id, frame } = await launch(page);
    await saveNewDocument(page, id, frame, 'Poster', 'first.designcraft');

    await menu(frame, 'app.saveDialog');
    await answerSaveDialog(page, SAVE_TITLE, 'second.designcraft');
    await expectTitle(page, id, 'second.designcraft — Kazo Design');

    // A plain save now writes second.designcraft in place.
    await modify(frame);
    await expectTitle(page, id, '*second.designcraft — Kazo Design');
    await menu(frame, 'app.save');
    await expectTitle(page, id, 'second.designcraft — Kazo Design');
    assert.equal(await page.locator('section.file-dialog').count(), 0);

    // Save a Copy is an export: a copy where the user says, the document stays bound and modified.
    await modify(frame);
    await expectTitle(page, id, '*second.designcraft — Kazo Design');
    await menu(frame, 'app.saveCopyDialog');
    await answerSaveDialog(page, COPY_TITLE, 'copy.designcraft');
    await windowById(page, id).locator('.design-notice', { hasText: 'Copy saved: copy.designcraft' }).waitFor({ timeout: STEP_TIMEOUT_MS });
    await expectTitle(page, id, '*second.designcraft — Kazo Design');

    // Export PDF: DesignCraft's PDF options first (its OK, as a click would), then a copy too,
    // never a browser download.
    await menu(frame, 'app.exportPdf');
    await request(frame, 'ui.dialog.confirm');
    await answerSaveDialog(page, COPY_TITLE, 'poster.pdf');
    await windowById(page, id).locator('.design-notice', { hasText: 'Copy saved: poster.pdf' }).waitFor({ timeout: STEP_TIMEOUT_MS });

    const files = await listing(page, HOME, 'poster.pdf');
    for (const name of ['first.designcraft', 'second.designcraft', 'copy.designcraft']) {
        assert.match(files, new RegExp(name.replace('.', '\\.')));
    }

    assert.deepEqual(errors, []);
    await page.close();
});

test('closing with changes asks; Cancel keeps the window, Yes saves before closing', { timeout: 400_000 }, async () => {
    const { page, errors } = await bootPage(browser, host.baseUrl);
    const { id, frame } = await launch(page);
    await newModifiedDocument(frame, 'Leaflet');
    await expectTitle(page, id, '*Leaflet — Kazo Design');

    const close = windowById(page, id).locator('header.window-chrome .chrome-button-close');
    const question = page.locator('.message-box-dialog', { hasText: 'Save the changes' });

    // Cancel: nothing closes, nothing is lost.
    await close.click();
    await question.waitFor({ timeout: STEP_TIMEOUT_MS });
    await question.getByRole('button', { name: 'Cancel' }).click();
    await question.waitFor({ state: 'hidden', timeout: STEP_TIMEOUT_MS });
    assert.equal(await windowById(page, id).count(), 1);
    await waitForDocument(frame, doc => doc.dirty, 'still modified');

    // Yes, then Cancel in the save dialog: the window stays open, and the notice says so.
    await close.click();
    await question.waitFor({ timeout: STEP_TIMEOUT_MS });
    await question.getByRole('button', { name: 'Yes' }).click();
    const saveDialog = page.locator('section.file-dialog', { hasText: SAVE_TITLE });
    await saveDialog.waitFor({ timeout: STEP_TIMEOUT_MS });
    await saveDialog.getByRole('button', { name: 'Cancel' }).click();
    await windowById(page, id).locator('.design-notice', { hasText: 'was not saved' }).waitFor({ timeout: STEP_TIMEOUT_MS });
    assert.equal(await windowById(page, id).count(), 1);
    await waitForDocument(frame, doc => doc.dirty, 'still modified after the cancelled save');

    // Yes, then a file name: the document is written, then the window closes.
    await close.click();
    await question.waitFor({ timeout: STEP_TIMEOUT_MS });
    await question.getByRole('button', { name: 'Yes' }).click();
    await answerSaveDialog(page, SAVE_TITLE, 'closing.designcraft');
    await windowById(page, id).waitFor({ state: 'detached', timeout: STEP_TIMEOUT_MS });

    await runInTerminal(page, `xdg-open ${HOME}/closing.designcraft`);
    const reopened = await waitForWindow(page, 1);
    await expectTitle(page, reopened, 'closing.designcraft — Kazo Design');

    assert.deepEqual(errors, []);
    await page.close();
});

test('DesignCraft\'s Open shows the OS dialog, the picked file is bound, and a second open brings its tab back', { timeout: 400_000 }, async () => {
    const { page, errors } = await bootPage(browser, host.baseUrl);
    const { id, frame } = await launch(page);
    await saveNewDocument(page, id, frame, 'Picked', 'picked.designcraft');
    await windowById(page, id).locator('header.window-chrome .chrome-button-close').click();
    await windowById(page, id).waitFor({ state: 'detached', timeout: STEP_TIMEOUT_MS });

    const fresh = await launch(page);
    await menu(fresh.frame, 'app.openDialog');
    await pickInDialog(page, OPEN_TITLE, 'picked.designcraft');
    await waitForDocument(fresh.frame, doc => doc.path === 'picked.designcraft', 'picked.designcraft');
    await expectTitle(page, fresh.id, 'picked.designcraft — Kazo Design');

    // Saved in place: the picked file is the document's.
    await modify(fresh.frame);
    await expectTitle(page, fresh.id, '*picked.designcraft — Kazo Design');
    await menu(fresh.frame, 'app.save');
    await expectTitle(page, fresh.id, 'picked.designcraft — Kazo Design');
    assert.equal(await page.locator('section.file-dialog').count(), 0);

    // A new document in front, then picked.designcraft again: its tab comes back, no second copy opens.
    await newModifiedDocument(fresh.frame, 'Other');
    await expectTitle(page, fresh.id, '*Other — Kazo Design');
    await menu(fresh.frame, 'app.openDialog');
    await pickInDialog(page, OPEN_TITLE, 'picked.designcraft');
    await expectTitle(page, fresh.id, '*picked.designcraft — Kazo Design');
    const documents = await documentsOf(fresh.frame);
    assert.equal(documents.filter(doc => doc.path === 'picked.designcraft').length, 1);
    assert.equal(await designWindows(page).count(), 1);

    assert.deepEqual(errors, []);
    await page.close();
});

test('an IDML opens converted, its first save asks for a .designcraft next to it, and the IDML is never written', { timeout: 500_000 }, async () => {
    const { page, errors } = await bootPage(browser, host.baseUrl);
    await runInTerminal(page, `mkdir ${HOME}/Clients`);
    const { id, frame } = await launch(page);

    // An IDML made by DesignCraft's own export, in Clients (the export is a copy, ADR 0264).
    await newModifiedDocument(frame, 'Catalog');
    await menu(frame, 'app.exportIdml');
    const exportDialog = page.locator('section.file-dialog', { hasText: COPY_TITLE });
    await exportDialog.waitFor({ timeout: STEP_TIMEOUT_MS });
    await exportDialog.getByText('Clients', { exact: true }).dblclick();
    await exportDialog.getByText('This folder is empty.').waitFor({ timeout: STEP_TIMEOUT_MS });
    // Kazo OS's file view must finish watching the folder it entered before the dialog closes
    // (a known Kazo OS bug, also met by Kazo Photo); a person never types that fast.
    await page.waitForTimeout(1_000);
    await exportDialog.getByLabel('File name').fill('catalog.idml');
    await exportDialog.getByRole('button', { name: 'Save' }).click();
    await windowById(page, id).locator('.design-notice', { hasText: 'Copy saved: catalog.idml' }).waitFor({ timeout: STEP_TIMEOUT_MS });
    const before = await listing(page, `${HOME}/Clients`, 'catalog.idml');

    // Opened from the OS: a new window, the document converted with no file of its own.
    await runInTerminal(page, `xdg-open ${HOME}/Clients/catalog.idml`);
    const second = await waitForWindow(page, 2, [id]);
    const converted = await frameOf(page, second);
    await waitForDocument(converted, doc => doc.path === null, 'converted from catalog.idml');

    // Save asks where to put a .designcraft, in Clients.
    await modify(converted);
    await menu(converted, 'app.save');
    const saveDialog = page.locator('section.file-dialog', { hasText: SAVE_TITLE });
    await saveDialog.waitFor({ timeout: STEP_TIMEOUT_MS });
    await saveDialog.getByText('catalog.idml', { exact: true }).waitFor({ timeout: STEP_TIMEOUT_MS });
    await page.waitForTimeout(1_000);
    await saveDialog.getByLabel('File name').fill('catalog.designcraft');
    await saveDialog.getByRole('button', { name: 'Save' }).click();
    await saveDialog.waitFor({ state: 'hidden', timeout: STEP_TIMEOUT_MS });
    await expectTitle(page, second, 'catalog.designcraft — Kazo Design');

    // The next save rewrites the .designcraft; the IDML keeps its size and date.
    await modify(converted);
    await menu(converted, 'app.save');
    await expectTitle(page, second, 'catalog.designcraft — Kazo Design');
    const after = await listing(page, `${HOME}/Clients`, 'catalog.designcraft');
    const idmlLine = text => text.split('\n').find(line => line.includes('catalog.idml'));
    assert.equal(idmlLine(after), idmlLine(before));

    assert.deepEqual(errors, []);
    await page.close();
});

test('Load Styles gets the picked file\'s bytes and opens no tab', { timeout: 400_000 }, async () => {
    const { page, errors } = await bootPage(browser, host.baseUrl);
    const { id, frame } = await launch(page);
    await saveNewDocument(page, id, frame, 'Styles', 'styles.designcraft');
    await newModifiedDocument(frame, 'Target');

    await menu(frame, 'app.loadTextStyles');
    await pickInDialog(page, PICK_TITLE, 'styles.designcraft');
    // The file went to DesignCraft's Load Styles, not to a new tab.
    await page.waitForTimeout(2_000);
    const documents = await documentsOf(frame);
    assert.equal(documents.length, 2, JSON.stringify(documents));
    await expectTitle(page, id, '*Target — Kazo Design');

    assert.deepEqual(errors, []);
    await page.close();
});

test('an OS chord pressed in the page reaches the OS: Alt+Q closes through the close guard', { timeout: 400_000 }, async () => {
    const { page, errors } = await bootPage(browser, host.baseUrl);
    const { id, frame } = await launch(page);
    await newModifiedDocument(frame, 'Chord');
    await expectTitle(page, id, '*Chord — Kazo Design');

    // The keyboard is in DesignCraft's canvas, as when the user works on the page.
    await frame.locator('#designcraft_canvas').click({ position: { x: 600, y: 400 } });
    // Alt+Q is the OS's other Close chord (WindowKeyBindings); headless Chromium never delivers the
    // F4 of Alt+F4, from the page or from the OS.
    await page.keyboard.press('Alt+KeyQ');
    const question = page.locator('.message-box-dialog', { hasText: 'Save the changes' });
    await question.waitFor({ timeout: STEP_TIMEOUT_MS });
    await question.getByRole('button', { name: 'No' }).click();
    await windowById(page, id).waitFor({ state: 'detached', timeout: STEP_TIMEOUT_MS });

    assert.deepEqual(errors, []);
    await page.close();
});
