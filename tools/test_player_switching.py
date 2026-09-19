import os
import time
from playwright.sync_api import sync_playwright

def run_test():
    out_dir = r"C:\Users\XIA\.gemini\antigravity\brain\2200dbbd-4406-44b0-9a45-7f8f16f1d832\screenshots"
    os.makedirs(out_dir, exist_ok=True)

    with sync_playwright() as p:
        browser = p.chromium.launch()

        # Client 1 Context (Player 1)
        ctx1 = browser.new_context(
            viewport={"width": 844, "height": 390},
            device_scale_factor=2,
            is_mobile=True,
            has_touch=True
        )
        page1 = ctx1.new_page()
        page1.goto("http://localhost:27502/")
        page1.wait_for_timeout(800)

        # Client 2 Context (Player 2)
        ctx2 = browser.new_context(
            viewport={"width": 844, "height": 390},
            device_scale_factor=2,
            is_mobile=True,
            has_touch=True
        )
        page2 = ctx2.new_page()
        page2.goto("http://localhost:27502/")
        page2.wait_for_timeout(800)

        badge1 = page1.locator("#slot-badge").text_content()
        badge2 = page2.locator("#slot-badge").text_content()
        print(f"Initial State: Client 1 is {badge1}, Client 2 is {badge2}")

        # Client 1 opens Player Switcher modal
        page1.click("#slot-badge")
        page1.wait_for_timeout(400)
        page1.screenshot(path=os.path.join(out_dir, "player_switch_modal.png"))
        print("Captured player_switch_modal.png")

        # Client 1 clicks 'Request Swap' for Player 2 (row 1, target slot 1)
        # Find swap button for Player 2
        page1.evaluate("""() => {
            const rows = document.querySelectorAll('.player-slot-row');
            if (rows.length >= 2) {
                const btn = rows[1].querySelector('.btn-swap');
                if (btn) btn.click();
            }
        }""")
        page1.wait_for_timeout(500)

        # Verify Client 2 sees swap prompt modal
        is_prompt_visible = page2.evaluate("() => !document.getElementById('modal-swap-prompt').classList.contains('hidden')")
        prompt_text = page2.locator("#swap-prompt-desc").text_content()
        print(f"Client 2 received prompt: visible={is_prompt_visible}, text='{prompt_text}'")
        page2.screenshot(path=os.path.join(out_dir, "swap_prompt_modal.png"))
        print("Captured swap_prompt_modal.png")

        # Client 2 accepts the swap
        page2.click("#btn-accept-swap")
        page2.wait_for_timeout(800)

        # Verify swapped badges
        new_badge1 = page1.locator("#slot-badge").text_content()
        new_badge2 = page2.locator("#slot-badge").text_content()
        print(f"After Swap: Client 1 is {new_badge1}, Client 2 is {new_badge2}")

        page1.screenshot(path=os.path.join(out_dir, "after_swap_client1.png"))
        page2.screenshot(path=os.path.join(out_dir, "after_swap_client2.png"))

        # Test switching to an empty slot: Client 1 (now P2) switches to P3
        page1.click("#slot-badge")
        page1.wait_for_timeout(400)
        page1.evaluate("""() => {
            const rows = document.querySelectorAll('.player-slot-row');
            if (rows.length >= 3) {
                const btn = rows[2].querySelector('.btn-slot-action:not(.btn-swap)');
                if (btn) btn.click();
            }
        }""")
        page1.wait_for_timeout(600)
        empty_switched_badge1 = page1.locator("#slot-badge").text_content()
        print(f"After Empty Switch: Client 1 is {empty_switched_badge1}")

        ctx1.close()
        ctx2.close()
        browser.close()

if __name__ == "__main__":
    run_test()
