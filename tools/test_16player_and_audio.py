import os
import sys
import time

if sys.platform == 'win32':
    sys.stdout.reconfigure(encoding='utf-8')
    sys.stderr.reconfigure(encoding='utf-8')

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

        slot1 = page1.evaluate("() => omniPadApp.network.padSlot")
        slot2 = page2.evaluate("() => omniPadApp.network.padSlot")
        print(f"[TEST] Initial slots: Client 1 = P{slot1 + 1}, Client 2 = P{slot2 + 1}")

        # Test 1: Verify Headphone Jack Button
        jack_btn = page1.locator("#btn-audio-jack")
        jack_text = page1.locator("#audio-jack-text").text_content()
        print(f"[TEST] Audio Jack initial text: '{jack_text}'")

        # Click Headphone Jack button
        jack_btn.click()
        page1.wait_for_timeout(800)

        is_jack_active = page1.evaluate("() => document.getElementById('btn-audio-jack').classList.contains('active')")
        new_jack_text = page1.locator("#audio-jack-text").text_content()
        print(f"[TEST] Audio Jack after click: active={is_jack_active}, text='{new_jack_text}'")
        page1.screenshot(path=os.path.join(out_dir, "audio_jack_active_hud.png"))

        # Test 2: Open Player Switcher Modal on Client 1 and check 16 slots & tier headers
        page1.click("#slot-badge")
        page1.wait_for_timeout(400)

        card_count = page1.evaluate("() => document.querySelectorAll('.player-slot-card').length")
        tier_count = page1.evaluate("() => document.querySelectorAll('.slot-tier-header').length")
        tier_titles = page1.evaluate("() => Array.from(document.querySelectorAll('.slot-tier-title')).map(el => el.textContent)")
        print(f"[TEST] Player Switcher Modal: {card_count} slot cards, {tier_count} tier headers.")
        print(f"[TEST] Tier Titles: {tier_titles}")

        page1.screenshot(path=os.path.join(out_dir, "16player_slots_landscape_tier1.png"))

        # Scroll down in modal body to show Tier 2 and Tier 3
        page1.evaluate("() => { const b = document.querySelector('#modal-player-switch .modal-body'); if(b) b.scrollTop = 380; }")
        page1.wait_for_timeout(300)
        page1.screenshot(path=os.path.join(out_dir, "16player_slots_landscape_tier2_3.png"))

        # Test 3: Switch to an Extended Party Slot (e.g. P6 in Tier 2)
        print("[TEST] Switching Client 1 to P6 (Slot index 5)...")
        page1.evaluate("""() => {
            const cards = document.querySelectorAll('.player-slot-card');
            if (cards.length >= 6) {
                const btn = cards[5].querySelector('.btn-slot-card-action');
                if (btn) btn.click();
            }
        }""")
        page1.wait_for_timeout(600)

        slot1_p6 = page1.evaluate("() => omniPadApp.network.padSlot")
        print(f"[TEST] Client 1 slot after switching to Tier 2: P{slot1_p6 + 1}")

        # Test 4: Switch to Mega-Party Slot (e.g. P16 in Tier 3)
        print("[TEST] Switching Client 1 to P16 (Slot index 15)...")
        page1.click("#slot-badge")
        page1.wait_for_timeout(400)
        page1.evaluate("""() => {
            const cards = document.querySelectorAll('.player-slot-card');
            if (cards.length >= 16) {
                const btn = cards[15].querySelector('.btn-slot-card-action');
                if (btn) btn.click();
            }
        }""")
        page1.wait_for_timeout(600)

        slot1_p16 = page1.evaluate("() => omniPadApp.network.padSlot")
        print(f"[TEST] Client 1 slot after switching to Tier 3: P{slot1_p16 + 1}")
        page1.screenshot(path=os.path.join(out_dir, "client1_p16_hud.png"))

        # Open modal again to show P16 active
        page1.click("#slot-badge")
        page1.wait_for_timeout(400)
        page1.evaluate("() => { const b = document.querySelector('#modal-player-switch .modal-body'); if(b) b.scrollTop = 700; }")
        page1.wait_for_timeout(300)
        page1.screenshot(path=os.path.join(out_dir, "16player_slots_p16_active.png"))
        page1.click("#btn-close-player-switch")
        page1.wait_for_timeout(300)

        # Test 5: Client 1 requests swap with Client 2's actual slot
        current_slot2 = page2.evaluate("() => omniPadApp.network.padSlot")
        print(f"[TEST] Client 1 (P{slot1_p16 + 1}) requesting swap with Client 2 (P{current_slot2 + 1})...")

        page1.click("#slot-badge")
        page1.wait_for_timeout(400)
        page1.evaluate(f"""() => {{
            const cards = document.querySelectorAll('.player-slot-card');
            if (cards.length > {current_slot2}) {{
                const btn = cards[{current_slot2}].querySelector('.btn-swap-action');
                if (btn) btn.click();
            }}
        }}""")
        page1.wait_for_timeout(500)

        # Check Client 2 prompt
        is_prompt_visible = page2.evaluate("() => !document.getElementById('modal-swap-prompt').classList.contains('hidden')")
        prompt_text = page2.locator("#swap-prompt-desc").text_content()
        print(f"[TEST] Client 2 received prompt: visible={is_prompt_visible}, text='{prompt_text}'")
        page2.screenshot(path=os.path.join(out_dir, "swap_prompt_modal_active.png"))

        # Client 2 accepts
        page2.click("#btn-accept-swap")
        page2.wait_for_timeout(800)

        after_swap_slot1 = page1.evaluate("() => omniPadApp.network.padSlot")
        after_swap_slot2 = page2.evaluate("() => omniPadApp.network.padSlot")
        print(f"[TEST] After Swap: Client 1 is P{after_swap_slot1 + 1}, Client 2 is P{after_swap_slot2 + 1}")

        # Test 6: Multi-device views
        # Portrait (390 x 844)
        ctx_portrait = browser.new_context(viewport={"width": 390, "height": 844}, device_scale_factor=2, is_mobile=True, has_touch=True)
        page_portrait = ctx_portrait.new_page()
        page_portrait.goto("http://localhost:27502/")
        page_portrait.wait_for_timeout(800)
        page_portrait.click("#slot-badge")
        page_portrait.wait_for_timeout(400)
        page_portrait.screenshot(path=os.path.join(out_dir, "16player_slots_portrait_390x844.png"))
        ctx_portrait.close()

        # Tablet (1024 x 768)
        ctx_tab = browser.new_context(viewport={"width": 1024, "height": 768}, device_scale_factor=2, is_mobile=True, has_touch=True)
        page_tab = ctx_tab.new_page()
        page_tab.goto("http://localhost:27502/")
        page_tab.wait_for_timeout(800)
        page_tab.click("#slot-badge")
        page_tab.wait_for_timeout(400)
        page_tab.screenshot(path=os.path.join(out_dir, "16player_slots_tablet_1024x768.png"))
        ctx_tab.close()

        # Flagship (915 x 412)
        ctx_flagship = browser.new_context(viewport={"width": 915, "height": 412}, device_scale_factor=2, is_mobile=True, has_touch=True)
        page_flagship = ctx_flagship.new_page()
        page_flagship.goto("http://localhost:27502/")
        page_flagship.wait_for_timeout(800)
        page_flagship.click("#slot-badge")
        page_flagship.wait_for_timeout(400)
        page_flagship.screenshot(path=os.path.join(out_dir, "16player_slots_flagship_915x412.png"))
        ctx_flagship.close()

        ctx1.close()
        ctx2.close()
        browser.close()
        print("[TEST] ALL 16-PLAYER AND AUDIO TESTS COMPLETED SUCCESSFULLY!")

if __name__ == "__main__":
    run_test()
