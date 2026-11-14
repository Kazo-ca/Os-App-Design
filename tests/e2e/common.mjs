// Shared helpers of the end-to-end runs: the browser, the Kazo Design windows and what runs in
// their iframes (DesignCraft's page).
import { chromium } from 'playwright';
import { boot } from './host.mjs';

// DesignCraft's first start downloads about 40 MB of .wasm from the local host and compiles it.
export const START_TIMEOUT_MS = 120_000;

// A software WebGPU adapter (SwiftShader), so both of DesignCraft's renderers can be tried.
export const launchBrowser = () => chromium.launch({
    args: ['--enable-unsafe-webgpu', '--enable-features=Vulkan', '--use-vulkan=swiftshader', '--use-webgpu-adapter=swiftshader', '--use-angle=vulkan'],
});

/** A fresh page with the OS booted; `errors` collects the page's uncaught errors. */
export async function bootPage(browser, baseUrl) {
    const page = await browser.newPage({ viewport: { width: 1400, height: 900 } });
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
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
