import os
import sys

if sys.platform == 'win32':
    sys.stdout.reconfigure(encoding='utf-8')

from playwright.sync_api import sync_playwright

p = sync_playwright().start()
b = p.chromium.launch()
ctx = b.new_context(viewport={'width': 844, 'height': 390}, has_touch=True, is_mobile=True)
page = ctx.new_page()
page.goto('http://localhost:27502/')
page.wait_for_timeout(500)

# Open Player Switch modal
page.click('#slot-badge')
page.wait_for_timeout(300)

initial_scroll = page.evaluate("() => document.querySelector('.player-switch-body').scrollTop")
print(f"Initial scrollTop: {initial_scroll}")

# 1. Test touch swipe up from 422, 280 to 422, 120
page.evaluate("""() => {
    const el = document.querySelector('.player-switch-body');
    const target = document.elementFromPoint(422, 280);

    const touchStart = new Touch({ identifier: 1, target: target, clientX: 422, clientY: 280, pageX: 422, pageY: 280 });
    target.dispatchEvent(new TouchEvent('touchstart', { touches: [touchStart], targetTouches: [touchStart], changedTouches: [touchStart], bubbles: true }));

    const touchMove = new Touch({ identifier: 1, target: target, clientX: 422, clientY: 100, pageX: 422, pageY: 100 });
    target.dispatchEvent(new TouchEvent('touchmove', { touches: [touchMove], targetTouches: [touchMove], changedTouches: [touchMove], bubbles: true }));

    const touchEnd = new Touch({ identifier: 1, target: target, clientX: 422, clientY: 100, pageX: 422, pageY: 100 });
    target.dispatchEvent(new TouchEvent('touchend', { touches: [], targetTouches: [], changedTouches: [touchEnd], bubbles: true }));
}""")

page.wait_for_timeout(300)
after_touch_scroll = page.evaluate("() => document.querySelector('.player-switch-body').scrollTop")
print(f"After touch swipe scrollTop: {after_touch_scroll}")

# 2. Test mouse wheel scroll
page.mouse.move(422, 200)
page.mouse.wheel(0, 400)
page.wait_for_timeout(300)
after_wheel_scroll = page.evaluate("() => document.querySelector('.player-switch-body').scrollTop")
print(f"After mouse wheel scrollTop: {after_wheel_scroll}")

# 3. Take screenshot of scrolled modal
out_dir = r"C:\Users\XIA\.gemini\antigravity\brain\2200dbbd-4406-44b0-9a45-7f8f16f1d832\screenshots"
page.screenshot(path=os.path.join(out_dir, "scrolled_modal_verification.png"))
print("Captured scrolled_modal_verification.png")

b.close()
p.stop()
