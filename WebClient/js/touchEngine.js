/**
 * OmniPad Touch & Input Engine
 * Multi-touch pointer tracking, floating dynamic joysticks, radial deadzones.
 */
class TouchEngine {
    constructor(stateCallback) {
        this.onStateChanged = stateCallback;

        // Pad State
        this.state = {
            buttons: 0,
            leftTrigger: 0,
            rightTrigger: 0,
            thumbLX: 0,
            thumbLY: 0,
            thumbRX: 0,
            thumbRY: 0
        };

        // Pointer Tracking: Map pointerId -> { controlType, element, ... }
        this.activePointers = new Map();

        // Settings
        this.deadzone = 0.08; // 8% inner deadzone
        this.floatingSticks = true;
        this.hapticsEnabled = true;

        // Button bitmasks
        this.BUTTONS = {
            DPAD_UP: 0x0001,
            DPAD_DOWN: 0x0002,
            DPAD_LEFT: 0x0004,
            DPAD_RIGHT: 0x0008,
            START: 0x0010,
            BACK: 0x0020,
            LS: 0x0040,
            RS: 0x0080,
            LB: 0x0100,
            RB: 0x0200,
            GUIDE: 0x0400,
            A: 0x1000,
            B: 0x2000,
            X: 0x4000,
            Y: 0x8000
        };

        // Keep-alive heartbeat: guarantees server connection stays active even when hands are off glass
        this.lastEmitTime = performance.now();
        setInterval(() => {
            if (performance.now() - this.lastEmitTime >= 300) {
                this.emitState();
            }
        }, 300);
    }

    triggerHaptic(duration = 12) {
        if (window.OmniPadNative && typeof window.OmniPadNative.vibrate === 'function') {
            try { window.OmniPadNative.vibrate(duration); return; } catch { }
        }
        if (this.hapticsEnabled && 'vibrate' in navigator) {
            try { navigator.vibrate(duration); } catch { }
        }
    }

    setButton(buttonMask, pressed) {
        if (pressed) {
            this.state.buttons |= buttonMask;
        } else {
            this.state.buttons &= ~buttonMask;
        }
        this.emitState();
    }

    setTrigger(triggerName, value) {
        const clamped = Math.max(0, Math.min(255, Math.round(value)));
        if (triggerName === 'LT') this.state.leftTrigger = clamped;
        if (triggerName === 'RT') this.state.rightTrigger = clamped;
        this.emitState();
    }

    setStick(stickName, normX, normY) {
        // Apply radial deadzone
        const magnitude = Math.sqrt(normX * normX + normY * normY);
        let finalX = 0;
        let finalY = 0;

        if (magnitude > this.deadzone) {
            // Scale between deadzone and 1.0
            const scaledMag = Math.min(1.0, (magnitude - this.deadzone) / (1.0 - this.deadzone));
            finalX = (normX / magnitude) * scaledMag;
            finalY = (normY / magnitude) * scaledMag;
        }

        const intX = Math.round(finalX * 32767);
        const intY = Math.round(finalY * 32767); // Positive is up

        if (stickName === 'left' || stickName === 'arcade') {
            this.state.thumbLX = intX;
            this.state.thumbLY = intY;
            if (stickName === 'arcade') {
                let dpadMask = 0;
                if (intY > 12000) dpadMask |= this.BUTTONS.DPAD_UP;
                if (intY < -12000) dpadMask |= this.BUTTONS.DPAD_DOWN;
                if (intX < -12000) dpadMask |= this.BUTTONS.DPAD_LEFT;
                if (intX > 12000) dpadMask |= this.BUTTONS.DPAD_RIGHT;
                this.state.buttons = (this.state.buttons & ~0x000F) | dpadMask;
            }
        } else if (stickName === 'right') {
            this.state.thumbRX = intX;
            this.state.thumbRY = intY;
        }

        this.emitState();
    }

    emitState() {
        this.lastEmitTime = performance.now();
        if (this.onStateChanged) {
            this.onStateChanged(this.state);
        }
    }

    // Attach listeners to DOM control elements
    bindControlElement(el, type, binding) {
        el.addEventListener('pointerdown', (e) => {
            e.preventDefault();
            el.setPointerCapture(e.pointerId);
            this.triggerHaptic(14);

            if (type === 'button') {
                el.classList.add('active');
                this.setButton(binding, true);
                this.activePointers.set(e.pointerId, { type, el, binding });
            } else if (type === 'trigger') {
                el.classList.add('active');
                this.setTrigger(binding, 255);
                this.activePointers.set(e.pointerId, { type, el, binding });
            } else if (type === 'joystick') {
                const rect = el.getBoundingClientRect();
                const radius = rect.width / 2;
                const centerX = rect.left + radius;
                const centerY = rect.top + radius;

                this.activePointers.set(e.pointerId, {
                    type,
                    el,
                    binding,
                    radius,
                    centerX,
                    centerY,
                    knob: el.querySelector('.joystick-knob')
                });
                this.updateJoystickPointer(e);
            }
        });

        el.addEventListener('pointermove', (e) => {
            if (!this.activePointers.has(e.pointerId)) return;
            const data = this.activePointers.get(e.pointerId);
            if (data.type === 'joystick') {
                this.updateJoystickPointer(e);
            }
        });

        const releasePointer = (e) => {
            if (!this.activePointers.has(e.pointerId)) return;
            const data = this.activePointers.get(e.pointerId);
            this.activePointers.delete(e.pointerId);

            if (data.type === 'button') {
                data.el.classList.remove('active');
                this.setButton(data.binding, false);
            } else if (data.type === 'trigger') {
                data.el.classList.remove('active');
                this.setTrigger(data.binding, 0);
            } else if (data.type === 'joystick') {
                if (data.knob) {
                    data.knob.style.transform = 'translate(-50%, -50%)';
                }
                this.setStick(data.binding, 0, 0);
                if (data.binding === 'arcade') {
                    this.state.buttons &= ~0x000F;
                    this.emitState();
                }
            }
        };

        el.addEventListener('pointerup', releasePointer);
        el.addEventListener('pointercancel', releasePointer);
    }

    updateJoystickPointer(e) {
        const data = this.activePointers.get(e.pointerId);
        if (!data) return;

        let dx = e.clientX - data.centerX;
        let dy = e.clientY - data.centerY;
        const dist = Math.sqrt(dx * dx + dy * dy);
        const maxRadius = data.radius * 0.75;

        let clampedDist = Math.min(dist, maxRadius);
        let angle = Math.atan2(dy, dx);

        let knobX = Math.cos(angle) * clampedDist;
        let knobY = Math.sin(angle) * clampedDist;

        if (data.knob) {
            data.knob.style.transform = `translate(calc(-50% + ${knobX}px), calc(-50% + ${knobY}px))`;
        }

        let normX = knobX / maxRadius;
        let normY = -(knobY / maxRadius); // Invert Y: Screen down is negative, controller up is positive
        this.setStick(data.binding, normX, normY);
    }

    // Unified Tactile D-Pad with wide cardinal zones and deliberate corner diagonals
    bindDpadElement(containerEl) {
        let activeMask = 0;

        const updateDpad = (e) => {
            const rect = containerEl.getBoundingClientRect();
            const centerX = rect.left + rect.width / 2;
            const centerY = rect.top + rect.height / 2;

            const dx = e.clientX - centerX;
            const dy = e.clientY - centerY;
            const dist = Math.sqrt(dx * dx + dy * dy);

            let newMask = 0;
            // Adapt inner deadzone dynamically to dpad spacing if customized
            let deadzone = 24;
            const customGap = containerEl.style.getPropertyValue('--dpad-gap');
            if (customGap) {
                deadzone = Math.max(12, parseInt(customGap) * 1.5);
            }

            if (dist > deadzone) {
                const angle = Math.atan2(dy, dx) * (180 / Math.PI); // -180 to 180 deg
                const isCornerPush = dist > (deadzone + 8);

                // Wide 70° cardinal sectors guarantee single-button accuracy
                if (angle >= -35 && angle <= 35) {
                    newMask = this.BUTTONS.DPAD_RIGHT;
                } else if (angle >= 55 && angle <= 125) {
                    newMask = this.BUTTONS.DPAD_DOWN;
                } else if (angle >= -125 && angle <= -55) {
                    newMask = this.BUTTONS.DPAD_UP;
                } else if (angle >= 145 || angle <= -145) {
                    newMask = this.BUTTONS.DPAD_LEFT;
                } else if (isCornerPush) {
                    // Deliberate 20° corner diagonals (only triggered when pressing into the corner)
                    if (angle > -55 && angle < -35) {
                        newMask = this.BUTTONS.DPAD_UP | this.BUTTONS.DPAD_RIGHT;
                    } else if (angle > 35 && angle < 55) {
                        newMask = this.BUTTONS.DPAD_DOWN | this.BUTTONS.DPAD_RIGHT;
                    } else if (angle > 125 && angle < 145) {
                        newMask = this.BUTTONS.DPAD_DOWN | this.BUTTONS.DPAD_LEFT;
                    } else if (angle > -145 && angle < -125) {
                        newMask = this.BUTTONS.DPAD_UP | this.BUTTONS.DPAD_LEFT;
                    }
                } else {
                    // Between sectors without deep push: snap to dominant cardinal
                    if (angle > -55 && angle < -35) {
                        newMask = (Math.abs(angle + 55) < Math.abs(angle + 35)) ? this.BUTTONS.DPAD_UP : this.BUTTONS.DPAD_RIGHT;
                    } else if (angle > 35 && angle < 55) {
                        newMask = (Math.abs(angle - 35) < Math.abs(angle - 55)) ? this.BUTTONS.DPAD_RIGHT : this.BUTTONS.DPAD_DOWN;
                    } else if (angle > 125 && angle < 145) {
                        newMask = (Math.abs(angle - 125) < Math.abs(angle - 145)) ? this.BUTTONS.DPAD_DOWN : this.BUTTONS.DPAD_LEFT;
                    } else if (angle > -145 && angle < -125) {
                        newMask = (Math.abs(angle + 125) < Math.abs(angle + 145)) ? this.BUTTONS.DPAD_UP : this.BUTTONS.DPAD_LEFT;
                    }
                }
            }

            if (newMask !== activeMask) {
                this.state.buttons = (this.state.buttons & ~0x000F) | newMask;
                activeMask = newMask;
                this.emitState();

                // Update visual glowing highlights on active wings
                containerEl.querySelector('.dpad-up')?.classList.toggle('active', (newMask & this.BUTTONS.DPAD_UP) !== 0);
                containerEl.querySelector('.dpad-down')?.classList.toggle('active', (newMask & this.BUTTONS.DPAD_DOWN) !== 0);
                containerEl.querySelector('.dpad-left')?.classList.toggle('active', (newMask & this.BUTTONS.DPAD_LEFT) !== 0);
                containerEl.querySelector('.dpad-right')?.classList.toggle('active', (newMask & this.BUTTONS.DPAD_RIGHT) !== 0);

                if (newMask !== 0) {
                    this.triggerHaptic(14);
                }
            }
        };

        const releaseDpad = () => {
            if (activeMask !== 0) {
                this.state.buttons &= ~0x000F;
                activeMask = 0;
                this.emitState();

                containerEl.querySelector('.dpad-up')?.classList.remove('active');
                containerEl.querySelector('.dpad-down')?.classList.remove('active');
                containerEl.querySelector('.dpad-left')?.classList.remove('active');
                containerEl.querySelector('.dpad-right')?.classList.remove('active');
            }
        };

        containerEl.addEventListener('pointerdown', (e) => {
            e.preventDefault();
            containerEl.setPointerCapture(e.pointerId);
            updateDpad(e);
        });

        containerEl.addEventListener('pointermove', (e) => {
            if (e.buttons > 0 || e.pressure > 0) {
                updateDpad(e);
            }
        });

        containerEl.addEventListener('pointerup', releaseDpad);
        containerEl.addEventListener('pointercancel', releaseDpad);
    }
}
