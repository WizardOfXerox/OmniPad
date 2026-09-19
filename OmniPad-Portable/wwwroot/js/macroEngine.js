/**
 * OmniPad Macro & Combo Engine
 * Simultaneous multi-button presses & millisecond-accurate timed sequence combos.
 */
class MacroEngine {
    constructor(touchEngine) {
        this.touchEngine = touchEngine;
        this.savedMacros = new Map();
        this.activeMacroCanceller = null;

        this.loadDefaultMacros();
    }

    loadDefaultMacros() {
        // Load saved macros from localStorage or initialize defaults
        const stored = localStorage.getItem('omnipad_macros');
        if (stored) {
            try {
                const parsed = JSON.parse(stored);
                parsed.forEach(m => this.savedMacros.set(m.id, m));
                return;
            } catch { }
        }

        // Default Fighting Game Macros (Tekken / SF)
        this.saveMacro({
            id: 'macro_ewgf',
            name: 'EWGF / Wind God Fist',
            steps: [
                { buttons: ['DPAD_DOWN'], holdMs: 25, delayMs: 20 },
                { buttons: ['DPAD_DOWN', 'DPAD_RIGHT'], holdMs: 25, delayMs: 15 },
                { buttons: ['DPAD_RIGHT', 'Y'], holdMs: 45, delayMs: 0 }
            ]
        });

        this.saveMacro({
            id: 'macro_slide_cancel',
            name: 'Shooter Slide Cancel',
            steps: [
                { buttons: ['B'], holdMs: 30, delayMs: 25 },
                { buttons: ['B'], holdMs: 30, delayMs: 20 },
                { buttons: ['A'], holdMs: 35, delayMs: 0 }
            ]
        });
    }

    saveMacro(macro) {
        this.savedMacros.set(macro.id, macro);
        this.persistMacros();
    }

    persistMacros() {
        const arr = Array.from(this.savedMacros.values());
        localStorage.setItem('omnipad_macros', JSON.stringify(arr));
    }

    // Trigger simultaneous combo (e.g. 1+2 = X+Y)
    triggerCombo(buttonNames, pressed) {
        let mask = 0;
        buttonNames.forEach(name => {
            if (this.touchEngine.BUTTONS[name]) {
                mask |= this.touchEngine.BUTTONS[name];
            }
        });

        this.touchEngine.setButton(mask, pressed);
    }

    // Execute timed sequential macro
    async executeMacro(macroId, loop = false) {
        this.cancelActiveMacro();

        const macro = this.savedMacros.get(macroId);
        if (!macro || !macro.steps || macro.steps.length === 0) return;

        let cancelled = false;
        this.activeMacroCanceller = () => { cancelled = true; };

        const runOnce = async () => {
            for (const step of macro.steps) {
                if (cancelled) break;

                // 1. Press step buttons
                let stepMask = 0;
                step.buttons.forEach(name => {
                    if (this.touchEngine.BUTTONS[name]) {
                        stepMask |= this.touchEngine.BUTTONS[name];
                    }
                });

                this.touchEngine.setButton(stepMask, true);

                // Hold duration
                await new Promise(r => setTimeout(r, step.holdMs || 25));
                this.touchEngine.setButton(stepMask, false);

                // Inter-step delay
                if (step.delayMs && step.delayMs > 0 && !cancelled) {
                    await new Promise(r => setTimeout(r, step.delayMs));
                }
            }
        };

        do {
            await runOnce();
        } while (loop && !cancelled);

        this.activeMacroCanceller = null;
    }

    cancelActiveMacro() {
        if (this.activeMacroCanceller) {
            this.activeMacroCanceller();
            this.activeMacroCanceller = null;
        }
    }
}
