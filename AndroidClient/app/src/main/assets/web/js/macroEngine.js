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
            } catch (e) { }
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
        let currentStepMask = 0;
        let activeTimeoutResolve = null;

        this.activeMacroCanceller = () => {
            cancelled = true;
            if (activeTimeoutResolve) {
                activeTimeoutResolve();
                activeTimeoutResolve = null;
            }
            if (currentStepMask !== 0) {
                this.touchEngine.setButton(currentStepMask, false);
                currentStepMask = 0;
            }
        };

        const sleep = (ms) => new Promise(r => {
            if (cancelled) return r();
            const timer = setTimeout(() => {
                activeTimeoutResolve = null;
                r();
            }, ms);
            activeTimeoutResolve = () => {
                clearTimeout(timer);
                r();
            };
        });

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

                currentStepMask = stepMask;
                this.touchEngine.setButton(stepMask, true);

                // Hold duration
                await sleep(step.holdMs || 25);
                this.touchEngine.setButton(stepMask, false);
                currentStepMask = 0;

                // Inter-step delay
                if (step.delayMs && step.delayMs > 0 && !cancelled) {
                    await sleep(step.delayMs);
                }
            }
        };

        do {
            await runOnce();
        } while (loop && !cancelled);

        if (currentStepMask !== 0) {
            this.touchEngine.setButton(currentStepMask, false);
            currentStepMask = 0;
        }

        this.activeMacroCanceller = null;
    }

    cancelActiveMacro() {
        if (this.activeMacroCanceller) {
            this.activeMacroCanceller();
            this.activeMacroCanceller = null;
        }
    }
}
