import os
import sys
import time

if sys.platform == 'win32':
    sys.stdout.reconfigure(encoding='utf-8')

from playwright.sync_api import sync_playwright

def run_test():
    with sync_playwright() as p:
        browser = p.chromium.launch()

        # Connect Client 1 (Player 1)
        ctx1 = browser.new_context()
        page1 = ctx1.new_page()
        page1.goto("http://localhost:27502/")
        page1.wait_for_timeout(600)

        # Connect Client 2 (Player 2)
        ctx2 = browser.new_context()
        page2 = ctx2.new_page()
        page2.goto("http://localhost:27502/")
        page2.wait_for_timeout(600)

        # Connect Client 3 (Player 3)
        ctx3 = browser.new_context()
        page3 = ctx3.new_page()
        page3.goto("http://localhost:27502/")
        page3.wait_for_timeout(600)

        slot_idx1 = page1.evaluate("() => window.omnipadApp ? window.omnipadApp.network.padSlot : -1")
        slot_idx2 = page2.evaluate("() => window.omnipadApp ? window.omnipadApp.network.padSlot : -1")
        slot_idx3 = page3.evaluate("() => window.omnipadApp ? window.omnipadApp.network.padSlot : -1")
        print(f"[TEST INITIAL] Connected 3 clients: C1=Slot {slot_idx1}, C2=Slot {slot_idx2}, C3=Slot {slot_idx3}")

        # Test Page Reload on Client 2: Verify it reclaims the EXACT same slot!
        print(f"[TEST RELOAD] Reloading Client 2 (Slot {slot_idx2})...")
        page2.reload()
        page2.wait_for_timeout(800)
        slot_idx2_reloaded = page2.evaluate("() => window.omnipadApp ? window.omnipadApp.network.padSlot : -1")
        print(f"[TEST RELOAD RESULT] Client 2 reconnected as Slot {slot_idx2_reloaded} (Expected: {slot_idx2})")
        assert slot_idx2_reloaded == slot_idx2, f"Expected Client 2 to retain Slot {slot_idx2}, but got {slot_idx2_reloaded}"

        # Check Client 1 sees Client 2 as In Use / Occupied
        page1.click("#slot-badge")
        page1.wait_for_timeout(300)
        statuses_before = page1.evaluate("""() => {
            const cards = document.querySelectorAll('.player-slot-card');
            return Array.from(cards).map(c => c.querySelector('.slot-status-pill').textContent);
        }""")
        print(f"[TEST BEFORE CLOSE] Slot {slot_idx2} status on Client 1: {statuses_before[slot_idx2]}")
        assert statuses_before[slot_idx2] in ("In Use", "Active")
        page1.click("#btn-close-player-switch")
        page1.wait_for_timeout(200)

        # Now close Client 2 completely (simulating browser tab close / app exit)
        print(f"[TEST CLOSE] Closing Client 2 browser tab/context (Slot {slot_idx2})...")
        ctx2.close()
        time.sleep(1.5)

        # Check Client 1's view of the slots: Client 2's slot MUST be 'Open' now!
        page1.click("#slot-badge")
        page1.wait_for_timeout(400)
        statuses_after = page1.evaluate("""() => {
            const cards = document.querySelectorAll('.player-slot-card');
            return Array.from(cards).map(c => c.querySelector('.slot-status-pill').textContent);
        }""")
        print(f"[TEST AFTER CLOSE] Slot {slot_idx2} status on Client 1: '{statuses_after[slot_idx2]}'")

        assert statuses_after[slot_idx2] == "Open", f"Expected Slot {slot_idx2} to be 'Open', but was '{statuses_after[slot_idx2]}'"

        out_dir = r"C:\Users\XIA\.gemini\antigravity\brain\2200dbbd-4406-44b0-9a45-7f8f16f1d832\screenshots"
        page1.screenshot(path=os.path.join(out_dir, "ghost_slot_fixed_verified.png"))
        print("[TEST] Captured ghost_slot_fixed_verified.png")

        ctx1.close()
        ctx3.close()
        browser.close()
        print("[TEST SUCCESS] Both page reload retention and instant slot freeing on disconnect verified!")

if __name__ == "__main__":
    run_test()
