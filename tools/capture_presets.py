import os
import time
from playwright.sync_api import sync_playwright

PRESETS = ["xbox", "playstation", "hitbox", "racing", "fps", "retro", "trackpad"]

def capture_all():
    out_dir = r"C:\Users\XIA\.gemini\antigravity\brain\2200dbbd-4406-44b0-9a45-7f8f16f1d832\screenshots"
    os.makedirs(out_dir, exist_ok=True)
    
    local_dir = r"h:\Ideas\Phone Gamepad\screenshots"
    os.makedirs(local_dir, exist_ok=True)

    with sync_playwright() as p:
        # Emulate modern mobile device in landscape
        browser = p.chromium.launch()
        context = browser.new_context(
            viewport={"width": 844, "height": 390},
            device_scale_factor=2,
            is_mobile=True,
            has_touch=True
        )
        page = context.new_page()

        for preset in PRESETS:
            url = f"http://localhost:27502/?preset={preset}"
            page.goto(url)
            # Give UI time to render and settle
            page.wait_for_timeout(800)

            brain_path = os.path.join(out_dir, f"{preset}.png")
            local_path = os.path.join(local_dir, f"{preset}.png")

            page.screenshot(path=brain_path)
            page.screenshot(path=local_path)
            print(f"Captured {preset} -> {brain_path}")

        browser.close()

if __name__ == "__main__":
    capture_all()
