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
page.click('#slot-badge')
page.wait_for_timeout(300)

initial_scroll = page.evaluate("() => document.querySelector('.player-switch-body').scrollTop")
print(f"Initial scrollTop: {initial_scroll}")

# Check touch-action on the element that will receive touch
touch_target_info = page.evaluate("""() => {
    const target = document.elementFromPoint(422, 250);
    return {
        tag: target.tagName,
        className: target.className,
        touchAction: getComputedStyle(target).touchAction
    };
}""")
print("Touch target info at (422, 250):", touch_target_info)

# Try dispatching touch swipe
page.evaluate("""() => {
    const el = document.querySelector('.player-switch-body');
    const target = document.elementFromPoint(422, 250);
    const touchObj = new Touch({
        identifier: 1,
        target: target,
        clientX: 422,
        clientY: 250,
        pageX: 422,
        pageY: 250,
        screenX: 422,
        screenY: 250
    });
    target.dispatchEvent(new TouchEvent('touchstart', { touches: [touchObj], targetTouches: [touchObj], changedTouches: [touchObj], bubbles: true, cancelable: true }));
}""")

b.close()
p.stop()
