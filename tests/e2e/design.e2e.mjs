// End-to-end runs of the Kazo Design window inside the Kazo OS host (doc/en/plan.md, work item 1):
// `npm run test:e2e` after `dotnet build Kazo.Design.slnx -c Debug`. Each test opens a fresh page.
//
// The window shows DesignCraft once DesignCraft says "ready" (designcraft:ready), which the host
// mode of our fork sends (fork work item F2, branch kazo/base).
import { after, before, test } from 'node:test';
import assert from 'node:assert/strict';
import { openApp, runInTerminal, startHost } from './host.mjs';
import { bootPage, designWindows, frameAt, graphicsFailed, launchBrowser, rendererOf, waitForDesignCraft } from './common.mjs';

let host;
let browser;

before(async () => {
    host = await startHost();
    browser = await launchBrowser();
});

after(async () => {
    await browser?.close();
    host?.stop();
});

test('Kazo Design opens from the Applications menu and DesignCraft starts in its iframe', { timeout: 300_000 }, async () => {
    const { page, errors } = await bootPage(browser, host.baseUrl);

    await openApp(page, 'Kazo Design');
    const frame = await frameAt(page, 0);
    const query = new URL(frame.url()).searchParams;
    assert.equal(query.has('control'), true, 'DesignCraft installs window.designcraft only with ?control');
    assert.equal(query.has('webgl'), false);

    await waitForDesignCraft(frame);

    assert.deepEqual(errors, []);
    await page.close();
});

test('the --webgl launch argument starts DesignCraft on WebGL 2', { timeout: 300_000 }, async () => {
    const { page, errors } = await bootPage(browser, host.baseUrl);

    await runInTerminal(page, 'launch ca.kazo.design --webgl');
    const frame = await frameAt(page, 0);
    assert.equal(new URL(frame.url()).searchParams.has('webgl'), true);
    await waitForDesignCraft(frame);
    assert.equal(await rendererOf(frame), 'webgl2');
    assert.equal(await graphicsFailed(frame), false, 'DesignCraft kept its graphics');

    assert.deepEqual(errors, []);
    await page.close();
});

test('two Kazo Design windows run their own DesignCraft at the same time', { timeout: 400_000 }, async () => {
    const { page, errors } = await bootPage(browser, host.baseUrl);

    await runInTerminal(page, 'launch ca.kazo.design --webgl');
    const first = await frameAt(page, 0);
    await waitForDesignCraft(first);
    await runInTerminal(page, 'launch ca.kazo.design --webgl');
    const second = await frameAt(page, 1);
    await waitForDesignCraft(second);

    assert.equal(await designWindows(page).count(), 2);
    assert.notEqual(first, second);
    assert.equal(await graphicsFailed(first), false);
    assert.equal(await graphicsFailed(second), false);

    assert.deepEqual(errors, []);
    await page.close();
});

test('the window shows DesignCraft once it is ready', { timeout: 300_000 }, async () => {
    const { page, errors } = await bootPage(browser, host.baseUrl);

    await openApp(page, 'Kazo Design');
    await page.locator('.design-frame.running').first().waitFor({ timeout: 120_000 });
    assert.equal(await page.locator('.design-screen').count(), 0, 'no loading or error screen is left');

    assert.deepEqual(errors, []);
    await page.close();
});
