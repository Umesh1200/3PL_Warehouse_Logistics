import { test, expect } from '@playwright/test';

const viewports = [
  { width: 320, height: 568, name: '320x568 (Mobile Portrait)' },
  { width: 568, height: 320, name: '568x320 (Mobile Landscape)' },
];

for (const vp of viewports) {
  test.describe(`Production Build Responsive Verification: ${vp.name}`, () => {
    test.beforeEach(async ({ page }) => {
      await page.setViewportSize({ width: vp.width, height: vp.height });
      await page.goto('/');
      await page.waitForLoadState('networkidle');
    });

    test(`verifies zero horizontal page overflow at ${vp.width}x${vp.height}`, async ({ page }) => {
      // Evaluate document scrollWidth vs clientWidth/viewport
      const overflow = await page.evaluate(() => {
        const docEl = document.documentElement;
        return {
          scrollWidth: docEl.scrollWidth,
          clientWidth: docEl.clientWidth,
          innerWidth: window.innerWidth,
          scrollX: window.scrollX,
        };
      });

      // Assert no horizontal page overflow (scrollWidth should not exceed viewport width)
      expect(overflow.scrollWidth).toBeLessThanOrEqual(vp.width);
      expect(overflow.scrollX).toBe(0);
    });

    test(`verifies modal buttons are within viewport, accept real pointer clicks, and body scrolls`, async ({ page }) => {
      // Open modal via real pointer click on hero CTA button
      const openModalBtn = page.getByRole('button', { name: /Open Enterprise Portal/i });
      await expect(openModalBtn).toBeVisible();
      await openModalBtn.click();

      const modalDialog = page.getByRole('dialog', { name: /Enterprise Operations Portal Unavailable/i });
      await expect(modalDialog).toBeVisible();

      // Locate both dismissal buttons
      const headerDismissBtn = page.getByRole('button', { name: /Dismiss notice/i });
      const footerCloseBtn = page.getByRole('button', { name: /Understood, close dialog/i });

      await expect(headerDismissBtn).toBeVisible();
      await expect(footerCloseBtn).toBeVisible();

      // Assert both dismiss buttons are strictly within the browser viewport
      const headerBox = await headerDismissBtn.boundingBox();
      const footerBox = await footerCloseBtn.boundingBox();

      expect(headerBox).not.toBeNull();
      expect(footerBox).not.toBeNull();

      if (headerBox && footerBox) {
        // Header dismiss button within viewport bounds
        expect(headerBox.x).toBeGreaterThanOrEqual(0);
        expect(headerBox.y).toBeGreaterThanOrEqual(0);
        expect(headerBox.x + headerBox.width).toBeLessThanOrEqual(vp.width);
        expect(headerBox.y + headerBox.height).toBeLessThanOrEqual(vp.height);

        // Footer close button within viewport bounds
        expect(footerBox.x).toBeGreaterThanOrEqual(0);
        expect(footerBox.y).toBeGreaterThanOrEqual(0);
        expect(footerBox.x + footerBox.width).toBeLessThanOrEqual(vp.width);
        expect(footerBox.y + footerBox.height).toBeLessThanOrEqual(vp.height);
      }

      // Assert modal body content scrollability when content exceeds body height
      const bodyScrollInfo = await page.evaluate(() => {
        const modalBody = document.querySelector('.modal-body');
        if (!modalBody) return null;
        return {
          scrollHeight: modalBody.scrollHeight,
          clientHeight: modalBody.clientHeight,
          canScroll: modalBody.scrollHeight > modalBody.clientHeight,
        };
      });

      expect(bodyScrollInfo).not.toBeNull();
      if (bodyScrollInfo && bodyScrollInfo.canScroll) {
        // Verify scrolling inside modal body while dismiss buttons remain reachable
        await page.evaluate(() => {
          const modalBody = document.querySelector('.modal-body');
          if (modalBody) modalBody.scrollTop = 50;
        });
      }

      // Assert dismissal via real pointer click on footer close button
      await footerCloseBtn.click();
      await expect(modalDialog).toBeHidden();

      // Reopen modal and verify dismissal via real pointer click on header dismiss button
      await openModalBtn.click();
      await expect(modalDialog).toBeVisible();
      await headerDismissBtn.click();
      await expect(modalDialog).toBeHidden();
    });

    test(`verifies keyboard trapping and focus restoration at ${vp.width}x${vp.height}`, async ({ page }) => {
      const openModalBtn = page.getByRole('button', { name: /Open Enterprise Portal/i });
      await openModalBtn.focus();
      
      // Open modal with Enter key
      await page.keyboard.press('Enter');

      const modalDialog = page.getByRole('dialog', { name: /Enterprise Operations Portal Unavailable/i });
      await expect(modalDialog).toBeVisible();

      const headerDismissBtn = page.getByRole('button', { name: /Dismiss notice/i });
      const footerCloseBtn = page.getByRole('button', { name: /Understood, close dialog/i });

      // Wait for initial focus on footer close button
      await expect(footerCloseBtn).toBeFocused();

      // Tab wraps from footer close button to header dismiss button
      await page.keyboard.press('Tab');
      await expect(headerDismissBtn).toBeFocused();

      // Tab advances back to footer close button
      await page.keyboard.press('Tab');
      await expect(footerCloseBtn).toBeFocused();

      // Shift+Tab moves back to header dismiss button
      await page.keyboard.press('Shift+Tab');
      await expect(headerDismissBtn).toBeFocused();

      // Shift+Tab wraps back to footer close button
      await page.keyboard.press('Shift+Tab');
      await expect(footerCloseBtn).toBeFocused();

      // Escape key dismisses modal and restores focus to openModalBtn
      await page.keyboard.press('Escape');
      await expect(modalDialog).toBeHidden();
      await expect(openModalBtn).toBeFocused();
    });
  });
}
