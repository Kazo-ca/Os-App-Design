// Shared helpers of the end-to-end runs: the browser, the Kazo Design windows and what runs in
// their iframes (DesignCraft's page).
import { chromium } from 'playwright';
import { boot } from './host.mjs';

// DesignCraft's first start downloads about 40 MB of .wasm from the local host and compiles it.
export const START_TIMEOUT_MS = 120_000;

// A software WebGPU adapter (SwiftShader), so both of DesignCraft's renderers can be tried. With
// `webgpu: false`, no WebGPU at all: DesignCraft starts on WebGL 2 however the OS launches it
// (xdg-open passes no --webgl), since a second DesignCraft on SwiftShader's WebGPU in the same
// page loses its device ("Graphics connection lost").
export const launchBrowser = ({ webgpu = true } = {}) => chromium.launch({
    args: webgpu
        ? ['--enable-unsafe-webgpu', '--enable-features=Vulkan', '--use-vulkan=swiftshader', '--use-webgpu-adapter=swiftshader', '--use-angle=vulkan']
        : ['--disable-features=WebGPU', '--use-angle=swiftshader'],
});

/** A fresh page with the OS booted; `errors` collects the page's uncaught errors. */
export async function bootPage(browser, baseUrl) {
    const page = await browser.newPage({ viewport: { width: 1400, height: 900 } });
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    if (process.env.DESIGN_E2E_CONSOLE) {
        const { appendFileSync } = await import('node:fs');
        page.on('console', message => ['error', 'warning'].includes(message.type()) && appendFileSync(process.env.DESIGN_E2E_CONSOLE, `${message.type()}: ${message.text()}\n----\n`));
    }
    await boot(page, baseUrl);
    return { page, errors };
}

/** Kazo Design's OS windows, in opening order. */
export const designWindows = page => page.locator('article[data-window-id]', { has: page.locator('.design-window') });

/** The iframe element of the window at `index` (opening order). */
export const iframeAt = (page, index) => designWindows(page).nth(index).locator('iframe.craft-frame-iframe');

/** The DesignCraft frame of the window at `index`, once the module put the iframe there. */
export async function frameAt(page, index) {
    const iframe = iframeAt(page, index);
    await iframe.waitFor({ state: 'attached', timeout: START_TIMEOUT_MS });
    return (await iframe.elementHandle()).contentFrame();
}

/**
 * Waits until DesignCraft runs in a frame: its start code removes #designcraft_loading once eframe
 * started on the canvas (apps/designcraft-web/src/web.rs), and the canvas then has a size.
 */
export async function waitForDesignCraft(frame) {
    await frame.waitForFunction(() => {
        const canvas = document.getElementById('designcraft_canvas');
        return document.getElementById('designcraft_loading') === null && canvas !== null && canvas.width > 0;
    }, undefined, { timeout: START_TIMEOUT_MS });
}

/** Whether DesignCraft showed its "Graphics connection lost" panel. */
export const graphicsFailed = frame => frame.evaluate(() => {
    const panel = document.getElementById('designcraft_graphics_error');
    return panel !== null && getComputedStyle(panel).display !== 'none';
});

/** The renderer DesignCraft's canvas uses: `webgpu`, `webgl2` or null. */
export const rendererOf = frame => frame.evaluate(() => {
    const canvas = document.getElementById('designcraft_canvas');
    if (canvas.getContext('webgpu')) {
        return 'webgpu';
    }
    return canvas.getContext('webgl2') ? 'webgl2' : null;
});

// The steps of a run that waits on the OS or on DesignCraft, once DesignCraft runs.
export const STEP_TIMEOUT_MS = 30_000;
export const HOME = '/home/neo';

/** Saves a screenshot of the page when DESIGN_E2E_SHOTS names a folder. */
export async function shot(page, name) {
    if (process.env.DESIGN_E2E_SHOTS) {
        await page.screenshot({ path: `${process.env.DESIGN_E2E_SHOTS}/${name}.png` });
    }
}

/** One OS window by its id. */
export const windowById = (page, id) => page.locator(`article[data-window-id="${id}"]`);

/** Waits for `count` Kazo Design windows to show DesignCraft; returns the id of the newest one. */
export async function waitForWindow(page, count, known = []) {
    try {
        await page.locator('.design-frame.running').nth(count - 1).waitFor({ timeout: START_TIMEOUT_MS });
    } catch (error) {
        await shot(page, `window-${count}`);
        throw error;
    }
    const ids = await designWindows(page).evaluateAll(nodes => nodes.map(node => node.getAttribute('data-window-id')));
    if (ids.length !== count) {
        throw new Error(`${ids.length} Kazo Design windows instead of ${count}`);
    }
    return ids.find(id => !known.includes(id));
}

/** The DesignCraft frame of a window, by the window's id. */
export async function frameOf(page, id) {
    const handle = await windowById(page, id).locator('iframe.craft-frame-iframe').elementHandle();
    return handle.contentFrame();
}

/** Runs a method of DesignCraft's control API (window.designcraft, installed with ?control) in a frame. */
export const request = (frame, method, params = {}) =>
    frame.evaluate(([name, args]) => window.designcraft.request(name, args), [method, params]);

/** Runs a DesignCraft engine command, as its menus do. */
export const execute = (frame, command, params = {}) => request(frame, 'engine.execute', { command, params });

/** Runs a DesignCraft menu item (File › Save…), which opens what the menu opens. */
export const menu = (frame, id) => request(frame, 'ui.menu.invoke', { id });

/** DesignCraft's open documents (document.list: index, uid, title, path, dirty, active). */
export async function documentsOf(frame) {
    const answer = await execute(frame, 'document.list');
    return Array.isArray(answer) ? answer : (answer?.result ?? []);
}

/** Waits until one of the frame's documents matches `predicate` (a function of a document.list entry). */
export async function waitForDocument(frame, predicate, description) {
    const deadline = Date.now() + STEP_TIMEOUT_MS;
    let last = [];
    while (Date.now() < deadline) {
        last = await documentsOf(frame).catch(error => [String(error)]);
        if (last.some(doc => typeof doc === 'object' && predicate(doc))) {
            return;
        }
        await new Promise(resolve => setTimeout(resolve, 200));
    }
    throw new Error(`no document ${description}: ${JSON.stringify(last)}`);
}

/** Creates a document named `title` with a text frame on its first page, so that it has unsaved changes. */
export async function newModifiedDocument(frame, title) {
    await execute(frame, 'file.new', { title });
    await execute(frame, 'frame.create', { rect: [36, 36, 300, 200], content: 'text' });
    await waitForDocument(frame, doc => doc.title === title && doc.dirty, `${title}, modified after file.new`);
}

/** Changes the document in front: one more frame. */
export const modify = frame => execute(frame, 'frame.create', { rect: [60, 260, 300, 400], content: 'text' });

/** Answers the OS save dialog with a file name (in the folder it opens in). */
export async function answerSaveDialog(page, title, fileName) {
    const dialog = page.locator('section.file-dialog', { hasText: title });
    await dialog.waitFor({ state: 'visible', timeout: STEP_TIMEOUT_MS });
    await dialog.getByLabel('File name').fill(fileName);
    await dialog.getByRole('button', { name: 'Save' }).click();
    await dialog.waitFor({ state: 'hidden', timeout: STEP_TIMEOUT_MS });
}

/** Waits for a window's title bar to read `text`; on failure, says what it reads and the window's notice. */
export async function expectTitle(page, id, text) {
    try {
        await page.waitForFunction(
            ([windowId, wanted]) => document.querySelector(`article[data-window-id="${windowId}"] header.window-chrome h2`)?.textContent?.trim() === wanted,
            [id, text],
            { timeout: STEP_TIMEOUT_MS });
    } catch (error) {
        const actual = await windowById(page, id).locator('header.window-chrome h2').first().textContent().catch(() => null);
        const notice = await windowById(page, id).locator('.design-notice').textContent().catch(() => null);
        await shot(page, `title-${id}`);
        throw new Error(`title "${actual}" instead of "${text}"; notice: ${notice}`, { cause: error });
    }
}
