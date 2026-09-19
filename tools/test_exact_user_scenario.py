import os
import sys
import time

if sys.platform == 'win32':
    sys.stdout.reconfigure(encoding='utf-8')

from playwright.sync_api import sync_playwright

def test_exact_user_flow():
    with sync_playwright() as p:
        browser = p.chromium.launch()

        # Tab A connects
        ctxA = browser.new_context()
        pageA = ctxA.new_page()
        pageA.goto("http://localhost:27502/")
        pageA.wait_for_timeout(500)
        slotA = pageA.evaluate("() => window.omnipadApp ? window.omnipadApp.network.padSlot : -1")
        print(f"[FLOW] Client A connected as Slot {slotA} (Player {slotA + 1})")

        # Tab B connects
        ctxB = browser.new_context()
        pageB = ctxB.new_page()
        pageB.goto("http://localhost:27502/")
        pageB.wait_for_timeout(500)
        slotB = pageB.evaluate("() => window.omnipadApp ? window.omnipadApp.network.padSlot : -1")
        print(f"[FLOW] Client B connected as Slot {slotB} (Player {slotB + 1})")

        # User restarts browser tab B (simulate F5 / page reload)
        print(f"[FLOW] Client B refreshes/restarts browser...")
        pageB.reload()
        pageB.wait_for_timeout(800)
        slotB_after_restart = pageB.evaluate("() => window.omnipadApp ? window.omnipadApp.network.padSlot : -1")
        print(f"[FLOW] Client B re-connected as Slot {slotB_after_restart} (Player {slotB_after_restart + 1})")

        # Must be the exact same slot!
        assert slotB_after_restart == slotB, f"Ghost slot bug! Client B jumped from Slot {slotB} to Slot {slotB_after_restart}!"

        # Now check Client A's view: Slot B should be 'In Use' or 'Active', NOT duplicated or orphaned
        pageA.click("#slot-badge")
        pageA.wait_for_timeout(300)
        statuses = pageA.evaluate("""() => {
            const cards = document.querySelectorAll('.player-slot-card');
            return Array.from(cards).map(c => c.querySelector('.slot-status-pill').textContent);
        }""")
        print(f"[FLOW] Slot statuses on Client A after B restart: Slot {slotA}={statuses[slotA]}, Slot {slotB}={statuses[slotB]}")
        assert statuses[slotB] in ("In Use", "Active")

        # Now user closes tab B completely
        print(f"[FLOW] Client B closes browser tab completely...")
        ctxB.close()
        time.sleep(1.5)

        # Re-fetch statuses on Client A
        pageA.click("#btn-close-player-switch")
        pageA.wait_for_timeout(200)
        pageA.click("#slot-badge")
        pageA.wait_for_timeout(300)
        statuses_closed = pageA.evaluate("""() => {
            const cards = document.querySelectorAll('.player-slot-card');
            return Array.from(cards).map(c => c.querySelector('.slot-status-pill').textContent);
        }""")
        print(f"[FLOW] Slot statuses on Client A after B closed: Slot {slotB} = '{statuses_closed[slotB]}'")
        assert statuses_closed[slotB] == "Open", f"Ghost slot! Slot {slotB} should be Open, but is '{statuses_closed[slotB]}'"

        out_dir = r"C:\Users\XIA\.gemini\antigravity\brain\2200dbbd-4406-44b0-9a45-7f8f16f1d832\screenshots"
        pageA.screenshot(path=os.path.join(out_dir, "user_flow_ghost_slot_verified.png"))
        print("[FLOW] Screenshot saved as user_flow_ghost_slot_verified.png")

        ctxA.close()
        browser.close()
        print("[FLOW TEST PASSED] Full user flow verified successfully!")

if __name__ == "__main__":
    test_exact_user_flow()
