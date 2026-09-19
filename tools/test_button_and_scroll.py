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

# Open modal
page.click('#slot-badge')
page.wait_for_timeout(300)

# 1. Tap on Switch to P6 button (Slot index 5)
print("[TEST] Tapping directly on Switch to P6 button...")
page.evaluate("""() => {
    const cards = document.querySelectorAll('.player-slot-card');
    if (cards.length >= 6) {
        const btn = cards[5].querySelector('.btn-slot-card-action');
        // Simulate pointerdown, pointerup at same spot
        btn.dispatchEvent(new PointerEvent('pointerdown', { clientX: 300, clientY: 200, bubbles: true }));
        btn.dispatchEvent(new PointerEvent('pointerup', { clientX: 300, clientY: 200, bubbles: true }));
    }
}""")

page.wait_for_timeout(600)
badge_after_tap = page.locator('#slot-badge').text_content()
print(f"[TEST] Slot badge after intentional tap on P6: {badge_after_tap.strip()}")

# 2. Re-open modal and simulate swipe-drag that starts on a button
page.click('#slot-badge')
page.wait_for_timeout(300)

print("[TEST] Simulating vertical swipe gesture that starts over a button...")
page.evaluate("""() => {
    const cards = document.querySelectorAll('.player-slot-card');
    const btn = cards[1].querySelector('.btn-slot-card-action'); // button for P2
    // User touches down on P2 button, but moves 60px upwards to scroll
    btn.dispatchEvent(new PointerEvent('pointerdown', { clientX: 300, clientY: 250, bubbles: true }));
    btn.dispatchEvent(new PointerEvent('pointermove', { clientX: 300, clientY: 190, bubbles: true }));
    btn.dispatchEvent(new PointerEvent('pointerup', { clientX: 300, clientY: 190, bubbles: true }));
}""")

page.wait_for_timeout(400)
# Verify modal is STILL OPEN (because the swipe was NOT treated as a button click!)
is_modal_still_open = page.evaluate("() => !document.getElementById('modal-player-switch').classList.contains('hidden')")
print(f"[TEST] Modal still open after swipe gesture (no accidental button activation): {is_modal_still_open}")

b.close()
p.stop()
