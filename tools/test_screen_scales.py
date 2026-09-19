import os
from playwright.sync_api import sync_playwright

VIEWPORTS = [
    {"name": "compact_phone_667x375", "width": 667, "height": 375},
    {"name": "standard_phone_844x390", "width": 844, "height": 390},
    {"name": "flagship_phone_915x412", "width": 915, "height": 412},
    {"name": "tablet_1024x768", "width": 1024, "height": 768}
]

def run_tests():
    out_dir = r"C:\Users\XIA\.gemini\antigravity\brain\2200dbbd-4406-44b0-9a45-7f8f16f1d832\screenshots"
    os.makedirs(out_dir, exist_ok=True)

    with sync_playwright() as p:
        browser = p.chromium.launch()

        for vp in VIEWPORTS:
            context = browser.new_context(
                viewport={"width": vp["width"], "height": vp["height"]},
                device_scale_factor=2,
                is_mobile=True,
                has_touch=True
            )
            page = context.new_page()
            url = "http://localhost:27502/?preset=xbox"
            page.goto(url)
            page.wait_for_timeout(600)

            # Check dynamic scale value computed by app
            dyn_scale = page.evaluate("() => document.getElementById('gamepad-container').style.getPropertyValue('--dyn-scale')")
            print(f"Viewport {vp['name']} ({vp['width']}x{vp['height']}) -> --dyn-scale: {dyn_scale}")

            shot_path = os.path.join(out_dir, f"scale_{vp['name']}.png")
            page.screenshot(path=shot_path)
            context.close()

        # Test controller size slider
        context = browser.new_context(
            viewport={"width": 844, "height": 390},
            device_scale_factor=2,
            is_mobile=True,
            has_touch=True
        )
        page = context.new_page()
        page.goto("http://localhost:27502/?preset=xbox")
        page.wait_for_timeout(600)

        # Change slider to 130%
        page.evaluate("""() => {
            const slider = document.getElementById('setting-controller-scale');
            if (slider) {
                slider.value = '130';
                slider.dispatchEvent(new Event('input'));
            }
        }""")
        page.wait_for_timeout(300)
        shot_path_130 = os.path.join(out_dir, "scale_slider_130.png")
        page.screenshot(path=shot_path_130)
        print("Captured scale_slider_130.png")

        context.close()
        browser.close()

if __name__ == "__main__":
    run_tests()
