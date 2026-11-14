// Measures decision 13 of doc/en/plan.md (work item 2): opening and saving a document whose
// embedded images weigh 30 to 50 MB, inside the Kazo OS host. Not part of `npm run test:e2e`
// (it takes minutes); run it by hand after `dotnet build Kazo.Design.slnx -c Debug`:
//
//   node tests/measure/large-document.mjs [megabytes]   (default 40)
//
// The images are PNGs of random pixels stored without compression, so the document cannot be
// smaller than they are. Each one is placed through file.place with its bytes, as the web build's
// File › Place does (the image is embedded: the web build has no file system to link to). Times
// are wall-clock times in headless Chromium with software rendering, from the action to the
// result the user sees: the title without its star after a save, the document listed after the
// OS opened the file.
import { deflateSync, crc32 } from 'node:zlib';
import { randomFillSync } from 'node:crypto';
import { runInTerminal, startHost } from '../e2e/host.mjs';
import {
    HOME, answerSaveDialog, bootPage, documentsOf, execute, frameOf, launchBrowser, menu, waitForWindow, windowById,
} from '../e2e/common.mjs';

const MEGABYTES = Number(process.argv[2] ?? 40);
const IMAGE_MEGABYTES = 10;
const WIDTH = 2048;
const LONG_MS = 600_000;

/** A PNG of random RGB pixels, about `megabytes` big, stored without compression. */
function noisePng(megabytes) {
    const height = Math.ceil((megabytes * 1_000_000) / (WIDTH * 3));
    const row = WIDTH * 3 + 1;
    const raw = Buffer.alloc(row * height);
    randomFillSync(raw);
    for (let y = 0; y < height; y++) {
        raw[y * row] = 0; // filter: none
    }
    const chunk = (type, data) => {
        const length = Buffer.alloc(4);
        length.writeUInt32BE(data.length);
        const body = Buffer.concat([Buffer.from(type, 'ascii'), data]);
        const crc = Buffer.alloc(4);
        crc.writeUInt32BE(crc32(body));
        return Buffer.concat([length, body, crc]);
    };
    const header = Buffer.alloc(13);
    header.writeUInt32BE(WIDTH, 0);
    header.writeUInt32BE(height, 4);
    header[8] = 8; // bit depth
    header[9] = 2; // RGB
    return Buffer.concat([
        Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
        chunk('IHDR', header),
        chunk('IDAT', deflateSync(raw, { level: 0 })),
        chunk('IEND', Buffer.alloc(0)),
    ]);
}

const seconds = start => ((performance.now() - start) / 1000).toFixed(1);

/** Waits for a window's title bar to read `text`, for up to ten minutes. */
const titleIs = (page, id, text) => page.waitForFunction(
    ([windowId, wanted]) => document.querySelector(`article[data-window-id="${windowId}"] header.window-chrome h2`)?.textContent?.trim() === wanted,
    [id, text],
    { timeout: LONG_MS });

const host = await startHost();
const browser = await launchBrowser({ webgpu: false });
try {
    const { page } = await bootPage(browser, host.baseUrl);
    await runInTerminal(page, 'launch ca.kazo.design --webgl');
    const id = await waitForWindow(page, 1);
    const frame = await frameOf(page, id);

    await execute(frame, 'file.new', { title: 'Large' });
    const count = Math.ceil(MEGABYTES / IMAGE_MEGABYTES);
    let placedBytes = 0;
    let start = performance.now();
    for (let i = 0; i < count; i++) {
        const png = noisePng(IMAGE_MEGABYTES);
        placedBytes += png.length;
        const answer = await execute(frame, 'file.place', { base64: png.toString('base64'), name: `noise-${i}.png`, x: 36 + 20 * i, y: 36 + 20 * i, width: 200 });
        if (answer?.error) {
            throw new Error(`file.place: ${JSON.stringify(answer.error)}`);
        }
    }
    console.log(`placed ${count} images, ${(placedBytes / 1e6).toFixed(1)} MB, in ${seconds(start)} s`);

    // DesignCraft's own share of a save: serializing the document (no OS involved).
    start = performance.now();
    const serialized = await execute(frame, 'file.serialize');
    console.log(`file.serialize alone: ${seconds(start)} s, ${((serialized?.bytes ?? serialized?.result?.bytes ?? 0) / 1e6).toFixed(1)} MB`);

    // First save: the OS dialog, then the write; done when the title loses its star.
    await menu(frame, 'app.save');
    await page.locator('section.file-dialog', { hasText: 'Save the document' }).waitFor({ timeout: LONG_MS });
    start = performance.now();
    await answerSaveDialog(page, 'Save the document', 'large.designcraft');
    await titleIs(page, id, 'large.designcraft — Kazo Design');
    console.log(`first save (from the dialog's Save button): ${seconds(start)} s`);

    // A second save in place, with no dialog.
    await execute(frame, 'frame.create', { rect: [60, 600, 300, 700], content: 'text' });
    await titleIs(page, id, '*large.designcraft — Kazo Design');
    start = performance.now();
    await menu(frame, 'app.save');
    await titleIs(page, id, 'large.designcraft — Kazo Design');
    console.log(`save in place: ${seconds(start)} s`);

    await runInTerminal(page, `ls -l ${HOME}`);
    await page.locator('.xterm-rows').last().getByText('large.designcraft').first().waitFor();
    const line = (await page.locator('.xterm-rows').last().innerText()).split('\n').find(text => text.includes('large.designcraft'));
    console.log(`file: ${line?.trim()}`);

    // Close the window (the document is saved), then open the file from the OS.
    await windowById(page, id).locator('header.window-chrome .chrome-button-close').click();
    await windowById(page, id).waitFor({ state: 'detached' });
    start = performance.now();
    await runInTerminal(page, `xdg-open ${HOME}/large.designcraft`);
    const reopened = await waitForWindow(page, 1);
    const running = seconds(start);
    const again = await frameOf(page, reopened);
    for (;;) {
        const documents = await documentsOf(again).catch(() => []);
        if (documents.some(doc => doc.path === 'large.designcraft')) {
            break;
        }
        if (performance.now() - start > LONG_MS) {
            throw new Error(`not opened: ${JSON.stringify(documents)}`);
        }
        await new Promise(resolve => setTimeout(resolve, 200));
    }
    console.log(`open from the OS: DesignCraft running after ${running} s, document listed after ${seconds(start)} s`);
    await page.close();
} finally {
    await browser.close();
    host.stop();
}
