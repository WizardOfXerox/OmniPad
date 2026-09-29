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
            TOUCHPAD: 0x0800,
            A: 0x1000,
            B: 0x2000,
            X: 0x4000,
            Y: 0x8000
        };

        this.onTouchpadChanged = null;
        this.onSpecialAction = null;

        // Programmable & Advanced Behaviors State
        this.latchedButtons = new Set();
        this.turboTimers = new Map();
        this.holdTimers = new Map();

        this.rafPending = false;
        this.isDirty = false;

        this.isEditing = false;

        // Keep-alive heartbeat: guarantees server connection stays active even when hands are off glass
        this.lastEmitTime = performance.now();
        setInterval(() => {
            if (performance.now() - this.lastEmitTime >= 300) {
                this.emitStateImmediate();
            }
        }, 300);
    }

    setEditMode(isEditing) {
        this.isEditing = !!isEditing;
        if (this.isEditing) {
            this.resetAll();
        }
    }

    isEditModeActive() {
        return this.isEditing || (typeof document !== 'undefined' && document.body && document.body.classList.contains('editing'));
    }

    resetAll() {
        this.state.buttons = 0;
        this.state.leftTrigger = 0;
        this.state.rightTrigger = 0;
        this.state.thumbLX = 0;
        this.state.thumbLY = 0;
        this.state.thumbRX = 0;
        this.state.thumbRY = 0;

        for (const [id, t] of this.turboTimers.entries()) {
            clearInterval(t.timer);
        }
        this.turboTimers.clear();

        for (const [id, h] of this.holdTimers.entries()) {
            clearTimeout(h.holdTimer);
        }
        this.holdTimers.clear();

        for (const el of this.latchedButtons) {
            el.classList.remove('active', 'latched');
        }
        this.latchedButtons.clear();

        this.activePointers.clear();

        if (typeof document !== 'undefined') {
            document.querySelectorAll('.control-elem.active, .control-elem.clicked, .control-elem.touch-active, .dpad-btn.active, .dpad-abxy-btn.active, .tp-quad-up.active, .tp-quad-down.active, .tp-quad-left.active, .tp-quad-right.active, .tp-abxy-y.active, .tp-abxy-b.active, .tp-abxy-a.active, .tp-abxy-x.active')
                .forEach(el => el.classList.remove('active', 'clicked', 'touch-active', 'latched'));

            document.querySelectorAll('.joystick-knob').forEach(knob => {
                knob.style.transform = 'translate(-50%, -50%)';
            });

            document.querySelectorAll('.trigger-fill, .pedal-fill').forEach(fill => {
                fill.style.height = '0%';
            });
        }

        this.emitStateImmediate();

        if (this.onTouchpadChanged) {
            this.onTouchpadChanged({
                clicked: false,
                finger0: { isActive: false, id: 0, x: 0, y: 0 },
                finger1: { isActive: false, id: 1, x: 0, y: 0 }
            });
        }
    }

    triggerHaptic(duration = 12) {
        if (window.OmniPadNative && typeof window.OmniPadNative.vibrate === 'function') {
            try { window.OmniPadNative.vibrate(duration); return; } catch (e) { }
        }
        if (this.hapticsEnabled && 'vibrate' in navigator) {
            try { navigator.vibrate(duration); } catch (e) { }
        }
    }

    setButton(buttonMask, pressed) {
        if (pressed) {
            this.state.buttons |= buttonMask;
        } else {
            this.state.buttons &= ~buttonMask;
        }
        this.emitStateImmediate();
    }

    setTrigger(triggerName, value) {
        const clamped = Math.max(0, Math.min(255, Math.round(value)));
        if (triggerName === 'LT') this.state.leftTrigger = clamped;
        if (triggerName === 'RT') this.state.rightTrigger = clamped;
        this.emitStateImmediate();
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

        this.requestFrameEmit();
    }

    emitStateImmediate() {
        this.isDirty = false;
        this.lastEmitTime = performance.now();
        if (this.onStateChanged) {
            this.onStateChanged(this.state);
        }
    }

    requestFrameEmit() {
        this.isDirty = true;
        if (!this.rafPending) {
            this.rafPending = true;
            requestAnimationFrame(() => {
                this.rafPending = false;
                if (this.isDirty) {
                    this.emitStateImmediate();
                }
            });
        }
    }

    emitState() {
        this.emitStateImmediate();
    }

    executeButtonAction(binding, isDown) {
        if (typeof binding === 'number') {
            this.setButton(binding, isDown);
        } else if (typeof binding === 'string') {
            if (binding === 'LT' || binding === 'RT' || binding === 'trigger_lt' || binding === 'trigger_rt') {
                const trig = (binding === 'LT' || binding === 'trigger_lt') ? 'LT' : 'RT';
                this.setTrigger(trig, isDown ? 255 : 0);
            } else if (binding.startsWith('paddle_p')) {
                const paddleMap = { 'paddle_p1': 0x1000, 'paddle_p2': 0x2000, 'paddle_p3': 0x4000, 'paddle_p4': 0x8000 };
                if (paddleMap[binding]) this.setButton(paddleMap[binding], isDown);
            } else if (binding.indexOf('fn_') === 0) {
                if (this.onSpecialAction) {
                    this.onSpecialAction(binding, isDown);
                }
            } else if (binding.indexOf(',') !== -1) {
                const parts = binding.split(',');
                for (let i = 0; i < parts.length; i++) {
                    const b = parseInt(parts[i].trim(), 16);
                    if (!isNaN(b)) this.setButton(b, isDown);
                }
            }
        } else if (Array.isArray(binding)) {
            for (let i = 0; i < binding.length; i++) {
                this.executeButtonAction(binding[i], isDown);
            }
        }
    }

    // Attach listeners to DOM control elements with programmable behaviors
    bindControlElement(el, type, binding, itemData) {
        itemData = itemData || {};

        el.addEventListener('pointerdown', (e) => {
            if (this.isEditModeActive()) return;
            e.preventDefault();
            try { el.setPointerCapture(e.pointerId); } catch (_) {}
            this.triggerHaptic(14);

            if (type === 'button') {
                // 1. Toggle / Latch Mode (Tap to lock ON, tap again to unlock OFF)
                if (itemData.behavior === 'toggle' || itemData.behavior === 'latch') {
                    if (this.latchedButtons.has(el)) {
                        this.latchedButtons.delete(el);
                        el.classList.remove('active', 'latched');
                        this.executeButtonAction(binding, false);
                        this.triggerHaptic(10);
                    } else {
                        this.latchedButtons.add(el);
                        el.classList.add('active', 'latched');
                        this.executeButtonAction(binding, true);
                        this.triggerHaptic(22);
                    }
                    return;
                }

                // 2. Turbo Mode (Continuous 20Hz rapid pulsing while held)
                if (itemData.behavior === 'turbo') {
                    el.classList.add('active');
                    let turboState = true;
                    this.executeButtonAction(binding, true);
                    const timer = setInterval(() => {
                        turboState = !turboState;
                        this.executeButtonAction(binding, turboState);
                    }, 50);
                    this.turboTimers.set(e.pointerId, { timer, el, binding });
                    this.activePointers.set(e.pointerId, { type, el, binding, itemData, isTurbo: true });
                    return;
                }

                // 3. Hold-Dual Action Mode (Tap = primary, Hold > 250ms = secondary)
                if (itemData.behavior === 'hold_dual' && itemData.secondaryBinding) {
                    el.classList.add('active');
                    const downTime = performance.now();
                    const holdTimer = setTimeout(() => {
                        this.triggerHaptic(25);
                        this.executeButtonAction(itemData.secondaryBinding, true);
                        el.classList.add('held-secondary');
                    }, 250);
                    this.holdTimers.set(e.pointerId, { holdTimer, downTime, firedSecondary: false });
                    this.activePointers.set(e.pointerId, { type, el, binding, itemData, isHoldDual: true });
                    return;
                }

                // 4. Standard Momentary Button
                el.classList.add('active');
                this.executeButtonAction(binding, true);
                this.activePointers.set(e.pointerId, { type, el, binding, itemData });
            } else if (type === 'trigger') {
                el.classList.add('active');
                this.setTrigger(binding, 255);
                this.activePointers.set(e.pointerId, { type, el, binding, itemData });
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
            if (this.isEditModeActive()) return;
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
                if (data.isTurbo && this.turboTimers.has(e.pointerId)) {
                    const t = this.turboTimers.get(e.pointerId);
                    clearInterval(t.timer);
                    this.turboTimers.delete(e.pointerId);
                    data.el.classList.remove('active');
                    this.executeButtonAction(data.binding, false);
                    return;
                }

                if (data.isHoldDual && this.holdTimers.has(e.pointerId)) {
                    const h = this.holdTimers.get(e.pointerId);
                    clearTimeout(h.holdTimer);
                    this.holdTimers.delete(e.pointerId);
                    data.el.classList.remove('active', 'held-secondary');
                    const elapsed = performance.now() - h.downTime;
                    if (elapsed >= 250) {
                        this.executeButtonAction(data.itemData.secondaryBinding, false);
                    } else {
                        // Short tap: fire primary for 60ms
                        this.executeButtonAction(data.binding, true);
                        setTimeout(() => this.executeButtonAction(data.binding, false), 60);
                    }
                    return;
                }

                data.el.classList.remove('active');
                this.executeButtonAction(data.binding, false);
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
            const container = document.getElementById('gamepad-container');
            const dynScale = container ? (parseFloat(getComputedStyle(container).getPropertyValue('--dyn-scale')) || 1.0) : 1.0;
            const elemScale = parseFloat(data.el.style.getPropertyValue('--elem-scale')) || 1.0;
            const totalScale = dynScale * elemScale;
            const renderX = totalScale > 0 ? (knobX / totalScale) : knobX;
            const renderY = totalScale > 0 ? (knobY / totalScale) : knobY;
            data.knob.style.transform = `translate(calc(-50% + ${renderX.toFixed(1)}px), calc(-50% + ${renderY.toFixed(1)}px))`;
        }

        let normX = knobX / maxRadius;
        let normY = -(knobY / maxRadius); // Invert Y: Screen down is negative, controller up is positive
        this.setStick(data.binding, normX, normY);
    }

    // Unified Tactile D-Pad with wide cardinal zones and deliberate corner diagonals
    bindDpadElement(containerEl) {
        let activeMask = 0;
        let centerX = 0;
        let centerY = 0;
        let maxRadius = 0;
        let deadzone = 24;

        const cacheBounds = () => {
            const rect = containerEl.getBoundingClientRect();
            centerX = rect.left + rect.width / 2;
            centerY = rect.top + rect.height / 2;
            maxRadius = Math.max(rect.width, rect.height) / 2;
            const customGap = containerEl.style.getPropertyValue('--dpad-gap');
            if (customGap) {
                deadzone = Math.max(12, parseInt(customGap) * 1.5);
            } else {
                deadzone = 24;
            }
        };

        const updateDpad = (e) => {
            const dx = e.clientX - centerX;
            const dy = e.clientY - centerY;
            const dist = Math.sqrt(dx * dx + dy * dy);

            let newMask = 0;
            const isOuterSide = dist > (deadzone + (maxRadius - deadzone) * 0.35);

            if (dist > deadzone) {
                const angle = Math.atan2(dy, dx) * (180 / Math.PI); // -180 to 180 deg

                // Outer circle zone gives generous 40° diagonal sectors for simultaneous dual-direction clicks
                if (isOuterSide) {
                    if (angle > -70 && angle < -20) {
                        newMask = this.BUTTONS.DPAD_UP | this.BUTTONS.DPAD_RIGHT;
                    } else if (angle > 20 && angle < 70) {
                        newMask = this.BUTTONS.DPAD_DOWN | this.BUTTONS.DPAD_RIGHT;
                    } else if (angle > 110 && angle < 160) {
                        newMask = this.BUTTONS.DPAD_DOWN | this.BUTTONS.DPAD_LEFT;
                    } else if (angle > -160 && angle < -110) {
                        newMask = this.BUTTONS.DPAD_UP | this.BUTTONS.DPAD_LEFT;
                    }
                }

                if (newMask === 0) {
                    if (angle >= -45 && angle <= 45) {
                        newMask = this.BUTTONS.DPAD_RIGHT;
                    } else if (angle >= 45 && angle <= 135) {
                        newMask = this.BUTTONS.DPAD_DOWN;
                    } else if (angle >= -135 && angle <= -45) {
                        newMask = this.BUTTONS.DPAD_UP;
                    } else {
                        newMask = this.BUTTONS.DPAD_LEFT;
                    }
                }
            }

            if (newMask !== activeMask) {
                this.state.buttons = (this.state.buttons & ~0x000F) | newMask;
                activeMask = newMask;
                this.emitStateImmediate();

                // Update visual glowing highlights on active wings
                const elUp = containerEl.querySelector('.dpad-up');
                if (elUp) elUp.classList.toggle('active', (newMask & this.BUTTONS.DPAD_UP) !== 0);
                const elDown = containerEl.querySelector('.dpad-down');
                if (elDown) elDown.classList.toggle('active', (newMask & this.BUTTONS.DPAD_DOWN) !== 0);
                const elLeft = containerEl.querySelector('.dpad-left');
                if (elLeft) elLeft.classList.toggle('active', (newMask & this.BUTTONS.DPAD_LEFT) !== 0);
                const elRight = containerEl.querySelector('.dpad-right');
                if (elRight) elRight.classList.toggle('active', (newMask & this.BUTTONS.DPAD_RIGHT) !== 0);

                if (newMask !== 0) {
                    this.triggerHaptic(14);
                }
            }
        };

        const releaseDpad = () => {
            if (activeMask !== 0) {
                this.state.buttons &= ~0x000F;
                activeMask = 0;
                this.emitStateImmediate();

                const elUp = containerEl.querySelector('.dpad-up');
                if (elUp) elUp.classList.remove('active');
                const elDown = containerEl.querySelector('.dpad-down');
                if (elDown) elDown.classList.remove('active');
                const elLeft = containerEl.querySelector('.dpad-left');
                if (elLeft) elLeft.classList.remove('active');
                const elRight = containerEl.querySelector('.dpad-right');
                if (elRight) elRight.classList.remove('active');
            }
        };

        containerEl.addEventListener('pointerdown', (e) => {
            if (this.isEditModeActive()) return;
            e.preventDefault();
            try { containerEl.setPointerCapture(e.pointerId); } catch (_) {}
            cacheBounds();
            updateDpad(e);
        });

        containerEl.addEventListener('pointermove', (e) => {
            if (this.isEditModeActive()) return;
            if (e.buttons > 0 || e.pressure > 0) {
                updateDpad(e);
            }
        });

        containerEl.addEventListener('pointerup', releaseDpad);
        containerEl.addEventListener('pointercancel', releaseDpad);
    }

    // Tactile D-Pad Style ABXY Cross (Y=Up, A=Down, X=Left, B=Right)
    // Touching direct cardinal zones fires single buttons; touching outer circle/sides fires dual-button chords (A+B, X+Y, Y+B, A+X)
    bindDpadAbxyElement(containerEl) {
        let activeMask = 0;
        let centerX = 0;
        let centerY = 0;
        let maxRadius = 0;
        let deadzone = 24;

        const cacheBounds = () => {
            const rect = containerEl.getBoundingClientRect();
            centerX = rect.left + rect.width / 2;
            centerY = rect.top + rect.height / 2;
            maxRadius = Math.max(rect.width, rect.height) / 2;
            const customGap = containerEl.style.getPropertyValue('--dpad-gap');
            if (customGap) {
                deadzone = Math.max(12, parseInt(customGap) * 1.5);
            } else {
                deadzone = 24;
            }
        };

        const updateDpadAbxy = (e) => {
            const dx = e.clientX - centerX;
            const dy = e.clientY - centerY;
            const dist = Math.sqrt(dx * dx + dy * dy);

            let newMask = 0;
            const isOuterSide = dist > (deadzone + (maxRadius - deadzone) * 0.35);

            if (dist > deadzone) {
                const angle = Math.atan2(dy, dx) * (180 / Math.PI); // -180 to 180 deg

                // Outer circle zone triggers simultaneous dual-button chords
                if (isOuterSide) {
                    if (angle > -70 && angle < -20) {
                        newMask = this.BUTTONS.Y | this.BUTTONS.B; // Top-Right: Y + B
                    } else if (angle > 20 && angle < 70) {
                        newMask = this.BUTTONS.A | this.BUTTONS.B; // Bottom-Right: A + B
                    } else if (angle > 110 && angle < 160) {
                        newMask = this.BUTTONS.A | this.BUTTONS.X; // Bottom-Left: A + X
                    } else if (angle > -160 && angle < -110) {
                        newMask = this.BUTTONS.Y | this.BUTTONS.X; // Top-Left: Y + X
                    }
                }

                if (newMask === 0) {
                    if (angle >= -45 && angle <= 45) {
                        newMask = this.BUTTONS.B; // East = B (Right)
                    } else if (angle >= 45 && angle <= 135) {
                        newMask = this.BUTTONS.A; // South = A (Bottom)
                    } else if (angle >= -135 && angle <= -45) {
                        newMask = this.BUTTONS.Y; // North = Y (Top)
                    } else {
                        newMask = this.BUTTONS.X; // West = X (Left)
                    }
                }
            }

            if (newMask !== activeMask) {
                const abxyClear = ~(this.BUTTONS.A | this.BUTTONS.B | this.BUTTONS.X | this.BUTTONS.Y);
                this.state.buttons = (this.state.buttons & abxyClear) | newMask;
                activeMask = newMask;
                this.emitStateImmediate();

                // Update visual glowing highlights on active wings
                const elY = containerEl.querySelector('.dpad-abxy-y');
                if (elY) elY.classList.toggle('active', (newMask & this.BUTTONS.Y) !== 0);
                const elA = containerEl.querySelector('.dpad-abxy-a');
                if (elA) elA.classList.toggle('active', (newMask & this.BUTTONS.A) !== 0);
                const elX = containerEl.querySelector('.dpad-abxy-x');
                if (elX) elX.classList.toggle('active', (newMask & this.BUTTONS.X) !== 0);
                const elB = containerEl.querySelector('.dpad-abxy-b');
                if (elB) elB.classList.toggle('active', (newMask & this.BUTTONS.B) !== 0);

                if (newMask !== 0) {
                    this.triggerHaptic(14);
                }
            }
        };

        const releaseDpadAbxy = () => {
            if (activeMask !== 0) {
                const abxyClear = ~(this.BUTTONS.A | this.BUTTONS.B | this.BUTTONS.X | this.BUTTONS.Y);
                this.state.buttons &= abxyClear;
                activeMask = 0;
                this.emitStateImmediate();

                const elY = containerEl.querySelector('.dpad-abxy-y');
                if (elY) elY.classList.remove('active');
                const elA = containerEl.querySelector('.dpad-abxy-a');
                if (elA) elA.classList.remove('active');
                const elX = containerEl.querySelector('.dpad-abxy-x');
                if (elX) elX.classList.remove('active');
                const elB = containerEl.querySelector('.dpad-abxy-b');
                if (elB) elB.classList.remove('active');
            }
        };

        containerEl.addEventListener('pointerdown', (e) => {
            if (this.isEditModeActive()) return;
            e.preventDefault();
            try { containerEl.setPointerCapture(e.pointerId); } catch (_) {}
            cacheBounds();
            updateDpadAbxy(e);
        });

        containerEl.addEventListener('pointermove', (e) => {
            if (this.isEditModeActive()) return;
            if (e.buttons > 0 || e.pressure > 0) {
                updateDpadAbxy(e);
            }
        });

        containerEl.addEventListener('pointerup', releaseDpadAbxy);
        containerEl.addEventListener('pointercancel', releaseDpadAbxy);
    }

    // High-resolution multi-touch trackpad (PS4/PS5 1920x942 coordinate space + gestures)
    bindTouchpadElement(containerEl) {
        const activeTouches = new Map();
        let isTouchpadClicked = false;
        let holdTimer = null;
        let containerRect = null;

        const emitTouchpadState = () => {
            const touchList = Array.from(activeTouches.values());
            const f0 = touchList[0] || { isActive: false, id: 0, x: 0, y: 0 };
            const f1 = touchList[1] || { isActive: false, id: 0, x: 0, y: 0 };

            const tpState = {
                clicked: isTouchpadClicked,
                finger0: {
                    isActive: !!touchList[0],
                    id: f0.id || 0,
                    x: f0.x || 0,
                    y: f0.y || 0
                },
                finger1: {
                    isActive: !!touchList[1],
                    id: f1.id || 0,
                    x: f1.x || 0,
                    y: f1.y || 0
                }
            };

            this.setButton(this.BUTTONS.TOUCHPAD, isTouchpadClicked);

            if (this.onTouchpadChanged) {
                this.onTouchpadChanged(tpState);
            }
        };

        containerEl.addEventListener('pointerdown', (e) => {
            if (this.isEditModeActive()) return;
            e.preventDefault();
            try { containerEl.setPointerCapture(e.pointerId); } catch (_) {}

            containerRect = containerEl.getBoundingClientRect();
            const normX = Math.max(0, Math.min(1, (e.clientX - containerRect.left) / containerRect.width));
            const normY = Math.max(0, Math.min(1, (e.clientY - containerRect.top) / containerRect.height));
            const tpX = Math.round(normX * 1920);
            const tpY = Math.round(normY * 942);

            const touchId = activeTouches.size;
            activeTouches.set(e.pointerId, {
                id: touchId,
                x: tpX,
                y: tpY,
                startX: tpX,
                startY: tpY,
                downTime: performance.now()
            });

            containerEl.classList.add('touch-active');
            this.triggerHaptic(10);

            // Hold-to-click gesture (> 450ms without large movement)
            if (holdTimer) clearTimeout(holdTimer);
            holdTimer = setTimeout(() => {
                if (activeTouches.has(e.pointerId)) {
                    const t = activeTouches.get(e.pointerId);
                    const dist = Math.abs(t.x - t.startX) + Math.abs(t.y - t.startY);
                    if (dist < 80) {
                        isTouchpadClicked = true;
                        containerEl.classList.add('clicked');
                        this.triggerHaptic(25);
                        emitTouchpadState();
                    }
                }
            }, 450);

            emitTouchpadState();
        });

        containerEl.addEventListener('pointermove', (e) => {
            if (this.isEditModeActive()) return;
            if (!activeTouches.has(e.pointerId)) return;
            if (!containerRect) containerRect = containerEl.getBoundingClientRect();
            const normX = Math.max(0, Math.min(1, (e.clientX - containerRect.left) / containerRect.width));
            const normY = Math.max(0, Math.min(1, (e.clientY - containerRect.top) / containerRect.height));
            const tpX = Math.round(normX * 1920);
            const tpY = Math.round(normY * 942);

            const t = activeTouches.get(e.pointerId);
            t.x = tpX;
            t.y = tpY;

            emitTouchpadState();
        });

        const handleTouchEnd = (e) => {
            if (!activeTouches.has(e.pointerId)) return;
            const t = activeTouches.get(e.pointerId);
            const duration = performance.now() - t.downTime;
            const dist = Math.abs(t.x - t.startX) + Math.abs(t.y - t.startY);

            if (holdTimer) clearTimeout(holdTimer);

            // Tap-to-click (< 250ms and < 60 units movement)
            if (duration < 250 && dist < 60 && !isTouchpadClicked) {
                isTouchpadClicked = true;
                containerEl.classList.add('clicked');
                this.triggerHaptic(18);
                emitTouchpadState();
                setTimeout(() => {
                    isTouchpadClicked = false;
                    containerEl.classList.remove('clicked');
                    emitTouchpadState();
                }, 80);
            } else if (isTouchpadClicked) {
                isTouchpadClicked = false;
                containerEl.classList.remove('clicked');
                emitTouchpadState();
            }

            activeTouches.delete(e.pointerId);
            if (activeTouches.size === 0) {
                containerEl.classList.remove('touch-active');
            }
            emitTouchpadState();
        };

        containerEl.addEventListener('pointerup', handleTouchEnd);
        containerEl.addEventListener('pointercancel', handleTouchEnd);
    }

    // Steam-Controller Style Touchpad as Directional Pad (4-way / 8-way Touch Surface)
    bindDpadTouchpad(containerEl) {
        let activeMask = 0;
        let cx = 0;
        let cy = 0;
        let radius = 0;
        const quads = {
            up: containerEl.querySelector('.tp-quad-up'),
            down: containerEl.querySelector('.tp-quad-down'),
            left: containerEl.querySelector('.tp-quad-left'),
            right: containerEl.querySelector('.tp-quad-right')
        };

        const cacheBounds = () => {
            const rect = containerEl.getBoundingClientRect();
            cx = rect.left + rect.width / 2;
            cy = rect.top + rect.height / 2;
            radius = rect.width / 2;
        };

        const updateTouch = (e) => {
            const dx = e.clientX - cx;
            const dy = e.clientY - cy;
            const dist = Math.sqrt(dx * dx + dy * dy);

            let newMask = 0;
            // 15% inner neutral deadzone
            if (dist > radius * 0.15) {
                const angle = Math.atan2(dy, dx) * (180 / Math.PI);
                if (angle >= -40 && angle <= 40) {
                    newMask = this.BUTTONS.DPAD_RIGHT;
                } else if (angle >= 50 && angle <= 130) {
                    newMask = this.BUTTONS.DPAD_DOWN;
                } else if (angle >= -130 && angle <= -50) {
                    newMask = this.BUTTONS.DPAD_UP;
                } else if (angle >= 140 || angle <= -140) {
                    newMask = this.BUTTONS.DPAD_LEFT;
                } else {
                    // Diagonals
                    if (angle > -50 && angle < -40) newMask = this.BUTTONS.DPAD_UP | this.BUTTONS.DPAD_RIGHT;
                    else if (angle > 40 && angle < 50) newMask = this.BUTTONS.DPAD_DOWN | this.BUTTONS.DPAD_RIGHT;
                    else if (angle > 130 && angle < 140) newMask = this.BUTTONS.DPAD_DOWN | this.BUTTONS.DPAD_LEFT;
                    else if (angle > -140 && angle < -130) newMask = this.BUTTONS.DPAD_UP | this.BUTTONS.DPAD_LEFT;
                }
            }

            if (newMask !== activeMask) {
                this.state.buttons = (this.state.buttons & ~0x000F) | newMask;
                activeMask = newMask;
                this.emitStateImmediate();
                this.triggerHaptic(8);

                // Update visual quadrant highlights
                if (quads.up) quads.up.classList.toggle('active', (newMask & this.BUTTONS.DPAD_UP) !== 0);
                if (quads.down) quads.down.classList.toggle('active', (newMask & this.BUTTONS.DPAD_DOWN) !== 0);
                if (quads.left) quads.left.classList.toggle('active', (newMask & this.BUTTONS.DPAD_LEFT) !== 0);
                if (quads.right) quads.right.classList.toggle('active', (newMask & this.BUTTONS.DPAD_RIGHT) !== 0);
            }
        };

        containerEl.addEventListener('pointerdown', (e) => {
            if (this.isEditModeActive()) return;
            e.preventDefault();
            try { containerEl.setPointerCapture(e.pointerId); } catch (_) {}
            cacheBounds();
            containerEl.classList.add('touch-active');
            updateTouch(e);
        });

        containerEl.addEventListener('pointermove', (e) => {
            if (this.isEditModeActive()) return;
            if (containerEl.classList.contains('touch-active')) {
                updateTouch(e);
            }
        });

        const handleRelease = (e) => {
            containerEl.classList.remove('touch-active');
            this.state.buttons &= ~0x000F;
            activeMask = 0;
            this.emitStateImmediate();
            if (quads.up) quads.up.classList.remove('active');
            if (quads.down) quads.down.classList.remove('active');
            if (quads.left) quads.left.classList.remove('active');
            if (quads.right) quads.right.classList.remove('active');
        };

        containerEl.addEventListener('pointerup', handleRelease);
        containerEl.addEventListener('pointercancel', handleRelease);
    }

    // Steam-Controller Style Touchpad as Face Buttons Diamond (North=Y, East=B, South=A, West=X)
    bindAbxyTouchpad(containerEl) {
        let activeMask = 0;
        let cx = 0;
        let cy = 0;
        let radius = 0;
        const quads = {
            y: containerEl.querySelector('.tp-abxy-y'),
            b: containerEl.querySelector('.tp-abxy-b'),
            a: containerEl.querySelector('.tp-abxy-a'),
            x: containerEl.querySelector('.tp-abxy-x')
        };

        const cacheBounds = () => {
            const rect = containerEl.getBoundingClientRect();
            cx = rect.left + rect.width / 2;
            cy = rect.top + rect.height / 2;
            radius = rect.width / 2;
        };

        const updateTouch = (e) => {
            const dx = e.clientX - cx;
            const dy = e.clientY - cy;
            const dist = Math.sqrt(dx * dx + dy * dy);

            let newMask = 0;
            if (dist > radius * 0.15) {
                const angle = Math.atan2(dy, dx) * (180 / Math.PI);
                if (angle >= -135 && angle <= -45) {
                    newMask = this.BUTTONS.Y;
                } else if (angle >= -45 && angle <= 45) {
                    newMask = this.BUTTONS.B;
                } else if (angle >= 45 && angle <= 135) {
                    newMask = this.BUTTONS.A;
                } else {
                    newMask = this.BUTTONS.X;
                }
            }

            if (newMask !== activeMask) {
                this.state.buttons = (this.state.buttons & ~0xF000) | newMask;
                activeMask = newMask;
                this.emitStateImmediate();
                this.triggerHaptic(10);

                if (quads.y) quads.y.classList.toggle('active', (newMask & this.BUTTONS.Y) !== 0);
                if (quads.b) quads.b.classList.toggle('active', (newMask & this.BUTTONS.B) !== 0);
                if (quads.a) quads.a.classList.toggle('active', (newMask & this.BUTTONS.A) !== 0);
                if (quads.x) quads.x.classList.toggle('active', (newMask & this.BUTTONS.X) !== 0);
            }
        };

        containerEl.addEventListener('pointerdown', (e) => {
            if (this.isEditModeActive()) return;
            e.preventDefault();
            try { containerEl.setPointerCapture(e.pointerId); } catch (_) {}
            cacheBounds();
            containerEl.classList.add('touch-active');
            updateTouch(e);
        });

        containerEl.addEventListener('pointermove', (e) => {
            if (this.isEditModeActive()) return;
            if (containerEl.classList.contains('touch-active')) {
                updateTouch(e);
            }
        });

        const handleRelease = (e) => {
            containerEl.classList.remove('touch-active');
            this.state.buttons &= ~0xF000;
            activeMask = 0;
            this.emitStateImmediate();
            if (quads.y) quads.y.classList.remove('active');
            if (quads.b) quads.b.classList.remove('active');
            if (quads.a) quads.a.classList.remove('active');
            if (quads.x) quads.x.classList.remove('active');
        };

        containerEl.addEventListener('pointerup', handleRelease);
        containerEl.addEventListener('pointercancel', handleRelease);
    }

    // Dedicated Scroll Wheel Touchpad (Vertical & Horizontal Smooth Strip with Detent Ticks)
    bindScrollTouchpad(containerEl, options) {
        options = options || {};
        let lastY = 0;
        let lastX = 0;
        let accumulatedY = 0;
        let isTouching = false;
        const tickStep = 18; // pixels per notch

        const indicator = containerEl.querySelector('.scroll-knob') || containerEl.querySelector('.scroll-indicator');

        containerEl.addEventListener('pointerdown', (e) => {
            if (this.isEditModeActive()) return;
            e.preventDefault();
            try { containerEl.setPointerCapture(e.pointerId); } catch (_) {}
            isTouching = true;
            lastY = e.clientY;
            lastX = e.clientX;
            accumulatedY = 0;
            containerEl.classList.add('scrolling');
            this.triggerHaptic(10);
        });

        containerEl.addEventListener('pointermove', (e) => {
            if (this.isEditModeActive()) return;
            if (!isTouching) return;
            const dy = e.clientY - lastY;
            lastY = e.clientY;
            accumulatedY += dy;

            // Visual feedback indicator offset
            if (indicator) {
                const offset = Math.max(-25, Math.min(25, dy * 2));
                indicator.style.transform = `translateY(${offset}px)`;
            }

            if (Math.abs(accumulatedY) >= tickStep) {
                const notches = Math.trunc(accumulatedY / tickStep);
                accumulatedY -= notches * tickStep;
                this.triggerHaptic(6);

                // Send 2-finger scroll coordinates to server TouchpadMouseEngine
                if (this.onTouchpadChanged) {
                    const scrollDelta = -notches * 30; // Negative dy = scroll up in Windows wheel
                    this.onTouchpadChanged({
                        touchCount: 2,
                        finger0: { isActive: true, id: 0, x: 960, y: 471 },
                        finger1: { isActive: true, id: 1, x: 960, y: 471 + scrollDelta }
                    });
                }
            }
        });

        const handleRelease = (e) => {
            if (!isTouching) return;
            isTouching = false;
            containerEl.classList.remove('scrolling');
            if (indicator) indicator.style.transform = 'translateY(0px)';
            if (this.onTouchpadChanged) {
                this.onTouchpadChanged({
                    touchCount: 0,
                    finger0: { isActive: false, id: 0, x: 0, y: 0 },
                    finger1: { isActive: false, id: 1, x: 0, y: 0 }
                });
            }
        };

        containerEl.addEventListener('pointerup', handleRelease);
        containerEl.addEventListener('pointercancel', handleRelease);
    }
}
