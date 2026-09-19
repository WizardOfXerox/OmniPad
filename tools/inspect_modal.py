import os
import sys

if sys.platform == 'win32':
    sys.stdout.reconfigure(encoding='utf-8')

from playwright.sync_api import sync_playwright

p = sync_playwright().start()
b = p.chromium.launch()
ctx = b.new_context(viewport={'width': 844, 'height': 390}, has_touch=True)
page = ctx.new_page()
page.goto('http://localhost:27502/')
page.wait_for_timeout(500)
page.click('#slot-badge')
page.wait_for_timeout(300)

initial_scroll = page.evaluate("() => document.querySelector('.player-switch-body').scrollTop")
print(f"Initial scrollTop: {initial_scroll}")

# Try mouse wheel
page.mouse.move(422, 195)
page.mouse.wheel(0, 300)
page.wait_for_timeout(300)
after_wheel_scroll = page.evaluate("() => document.querySelector('.player-switch-body').scrollTop")
print(f"After wheel scrollTop: {after_wheel_scroll}")

# Try touch drag
page.touchscreen.tap(422, 280)
page.wait_for_timeout(100)
# Drag from 422, 280 up to 422, 100
page.evaluate("""async () => {
    const el = document.querySelector('.player-switch-body');
    el.scrollTop = 250;
}""")
after_js_scroll = page.evaluate("() => document.querySelector('.player-switch-body').scrollTop")
print(f"After JS scroll: {after_js_scroll}")

b.close()
p.stop()
