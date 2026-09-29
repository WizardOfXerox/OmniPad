/**
 * OmniPad Mouse & Keyboard Engine
 * Provides on-screen Virtual Trackpad and Virtual Gaming Keyboard.
 * Sends binary input packets to PCServer via WebSocket or native Bluetooth HID.
 * Coexists seamlessly with physical keyboard and mouse connected to the PC.
 */
class MouseKeyboardEngine {
    constructor(networkClient) {
        this.network = networkClient;
        this.isOpen = false;
        this.activeMode = 'none'; // 'trackpad' or 'keyboard'

        // Trackpad Settings
        this.sensitivity = 1.4;
        this.scrollSensitivity = 1.2;
        this.tapToClick = true;

        // Pointer tracking for Trackpad
        this.pointers = new Map();
        this.activeMouseButtons = new Map(); // pointerId -> buttonMask (1 or 2)
        this.activeKeys = new Map(); // pointerId -> { keyEl, vkCode }
        this.lastTapTime = 0;
        this.isDragging = false;

        // Elements
        this.trackpadOverlay = null;
        this.keyboardDrawer = null;
        this.trackpadSurface = null;

        this.hasBoundTrackpad = false;
        this.hasBoundKeyboard = false;

        this.initDOMElements();
    }

    initDOMElements() {
        this.trackpadOverlay = document.getElementById('omnipad-trackpad-overlay');
        this.keyboardDrawer = document.getElementById('omnipad-virtual-keyboard');
        this.trackpadSurface = document.getElementById('trackpad-touch-surface');

        if (this.trackpadSurface && !this.hasBoundTrackpad) {
            this.bindTrackpadEvents();
        }

        if (this.keyboardDrawer && !this.hasBoundKeyboard) {
            this.bindKeyboardEvents();
        }

        const closeTp = document.getElementById('btn-close-trackpad');
        if (closeTp && !closeTp.dataset.bound) {
            closeTp.dataset.bound = 'true';
            closeTp.addEventListener('pointerdown', (e) => {
                e.preventDefault();
                e.stopPropagation();
                this.toggleTrackpad();
            });
        }

        const closeKb = document.getElementById('btn-close-keyboard');
        if (closeKb && !closeKb.dataset.bound) {
            closeKb.dataset.bound = 'true';
            closeKb.addEventListener('pointerdown', (e) => {
                e.preventDefault();
                e.stopPropagation();
                this.toggleKeyboard();
            });
        }
    }

    // Toggle Trackpad Overlay ON / OFF
    toggleTrackpad() {
        if (!this.trackpadOverlay) this.initDOMElements();
        if (!this.trackpadOverlay) return;

        const isShowing = !this.trackpadOverlay.classList.contains('hidden');
        if (isShowing) {
            this.trackpadOverlay.classList.add('hidden');
            this.activeMode = 'none';
            // Safety release for any held mouse buttons or touches
            this.pointers.clear();
            for (const [, mask] of this.activeMouseButtons) {
                this.sendMouseButton(mask, false);
            }
            this.activeMouseButtons.clear();
            const btnL = document.getElementById('tp-btn-left-click');
            const btnR = document.getElementById('tp-btn-right-click');
            if (btnL) btnL.classList.remove('active');
            if (btnR) btnR.classList.remove('active');
        } else {
            this.trackpadOverlay.classList.remove('hidden');
            if (this.keyboardDrawer) this.keyboardDrawer.classList.add('hidden');
            this.activeMode = 'trackpad';
        }
        this.updateTopBarState();
    }

    // Toggle Virtual Keyboard Drawer ON / OFF
    toggleKeyboard() {
        if (!this.keyboardDrawer) this.initDOMElements();
        if (!this.keyboardDrawer) return;

        const isShowing = !this.keyboardDrawer.classList.contains('hidden');
        if (isShowing) {
            this.keyboardDrawer.classList.add('hidden');
            this.activeMode = 'none';
            // Safety release for any active held keyboard keys
            for (const [, item] of this.activeKeys) {
                if (item.keyEl) item.keyEl.classList.remove('active');
                this.sendKeyboardKey(item.vkCode, false);
            }
            this.activeKeys.clear();
            if (this.keyboardDrawer) {
                this.keyboardDrawer.querySelectorAll('.vk-key.active').forEach(k => k.classList.remove('active'));
            }
        } else {
            this.keyboardDrawer.classList.remove('hidden');
            if (this.trackpadOverlay) this.trackpadOverlay.classList.add('hidden');
            this.activeMode = 'keyboard';
        }
        this.updateTopBarState();
    }

    updateTopBarState() {
        const btnHudTp = document.getElementById('btn-hud-trackpad');
        const btnHudKb = document.getElementById('btn-hud-keyboard');
        const btnTbTp = document.getElementById('btn-topbar-trackpad');
        const btnTbKb = document.getElementById('btn-topbar-keyboard');
        const isTp = this.activeMode === 'trackpad';
        const isKb = this.activeMode === 'keyboard';

        if (btnHudTp) btnHudTp.classList.toggle('active', isTp);
        if (btnHudKb) btnHudKb.classList.toggle('active', isKb);
        if (btnTbTp) btnTbTp.classList.toggle('active', isTp);
        if (btnTbKb) btnTbKb.classList.toggle('active', isKb);
    }

    // =========================================================================
    // Trackpad Event Handling (Multi-touch with Cached Bounds & Zero Reflow)
    // =========================================================================
    bindTrackpadEvents() {
        if (!this.trackpadSurface || this.hasBoundTrackpad) return;
        this.hasBoundTrackpad = true;

        const surface = this.trackpadSurface;
        let lastX = 0;
        let lastY = 0;
        let downTime = 0;
        let totalDist = 0;
        let isTwoFinger = false;
        let prevF1Y = 0;

        surface.addEventListener('pointerdown', (e) => {
            e.preventDefault();
            try { surface.setPointerCapture(e.pointerId); } catch (_) {}

            this.pointers.set(e.pointerId, {
                startX: e.clientX,
                startY: e.clientY,
                currentX: e.clientX,
                currentY: e.clientY,
                time: performance.now()
            });

            if (this.pointers.size === 1) {
                lastX = e.clientX;
                lastY = e.clientY;
                downTime = performance.now();
                totalDist = 0;
                isTwoFinger = false;
            } else if (this.pointers.size === 2) {
                isTwoFinger = true;
                const arr = Array.from(this.pointers.values());
                prevF1Y = (arr[0].currentY + arr[1].currentY) / 2;
            }
        });

        surface.addEventListener('pointermove', (e) => {
            if (!this.pointers.has(e.pointerId)) return;
            const p = this.pointers.get(e.pointerId);
            p.currentX = e.clientX;
            p.currentY = e.clientY;

            if (this.pointers.size === 1) {
                // 1-finger relative mouse cursor move
                const dx = e.clientX - lastX;
                const dy = e.clientY - lastY;
                lastX = e.clientX;
                lastY = e.clientY;
                totalDist += Math.abs(dx) + Math.abs(dy);

                if (Math.abs(dx) > 0 || Math.abs(dy) > 0) {
                    const speed = Math.sqrt(dx * dx + dy * dy);
                    const accel = speed > 16 ? 2.0 : (speed > 8 ? 1.4 : 1.0);
                    const sendDx = Math.round(dx * accel * this.sensitivity);
                    const sendDy = Math.round(dy * accel * this.sensitivity);
                    this.sendMouseMove(sendDx, sendDy);
                }
            } else if (this.pointers.size === 2) {
                // 2-finger wheel scroll
                const arr = Array.from(this.pointers.values());
                const avgY = (arr[0].currentY + arr[1].currentY) / 2;
                const scrollDy = avgY - prevF1Y;
                prevF1Y = avgY;

                if (Math.abs(scrollDy) >= 2) {
                    // Touch down is positive, Windows scroll up is positive wheel
                    const wheelDelta = Math.round(-scrollDy * 12 * this.scrollSensitivity);
                    this.sendMouseWheel(wheelDelta);
                }
            }
        });

        const handlePointerUp = (e) => {
            if (!this.pointers.has(e.pointerId)) return;
            const p = this.pointers.get(e.pointerId);
            const duration = performance.now() - p.time;
            const pointerCount = this.pointers.size;
            this.pointers.delete(e.pointerId);

            // Tap detection
            if (duration < 240 && totalDist < 14) {
                if (pointerCount === 1 && !isTwoFinger) {
                    // Single finger tap = Left Click
                    this.sendMouseButton(1, true);
                    setTimeout(() => this.sendMouseButton(1, false), 40);
                    this.triggerHaptic(12);
                } else if (pointerCount === 2 || isTwoFinger) {
                    // Two finger tap = Right Click
                    this.sendMouseButton(2, true);
                    setTimeout(() => this.sendMouseButton(2, false), 40);
                    this.triggerHaptic(18);
                }
            }

            if (this.pointers.size === 0) {
                isTwoFinger = false;
                totalDist = 0;
            }
        };

        surface.addEventListener('pointerup', handlePointerUp);
        surface.addEventListener('pointercancel', handlePointerUp);

        // Hardware-style Bottom Action Buttons (Left Click / Right Click)
        const btnLeft = document.getElementById('tp-btn-left-click');
        const btnRight = document.getElementById('tp-btn-right-click');

        if (btnLeft) {
            btnLeft.addEventListener('pointerdown', (e) => {
                e.preventDefault();
                this.activeMouseButtons.set(e.pointerId, 1);
                btnLeft.classList.add('active');
                this.sendMouseButton(1, true);
                this.triggerHaptic(15);
            });
        }

        if (btnRight) {
            btnRight.addEventListener('pointerdown', (e) => {
                e.preventDefault();
                this.activeMouseButtons.set(e.pointerId, 2);
                btnRight.classList.add('active');
                this.sendMouseButton(2, true);
                this.triggerHaptic(15);
            });
        }

        // Window-level safety release for mouse buttons
        const handleGlobalMouseUp = (e) => {
            if (this.activeMouseButtons.has(e.pointerId)) {
                const mask = this.activeMouseButtons.get(e.pointerId);
                this.activeMouseButtons.delete(e.pointerId);
                if (mask === 1 && btnLeft) btnLeft.classList.remove('active');
                if (mask === 2 && btnRight) btnRight.classList.remove('active');
                this.sendMouseButton(mask, false);
            }
        };
        window.addEventListener('pointerup', handleGlobalMouseUp);
        window.addEventListener('pointercancel', handleGlobalMouseUp);
    }

    // =========================================================================
    // Virtual Keyboard Event Handling
    // =========================================================================
    bindKeyboardEvents() {
        if (!this.keyboardDrawer || this.hasBoundKeyboard) return;
        this.hasBoundKeyboard = true;

        const keys = this.keyboardDrawer.querySelectorAll('.vk-key');
        keys.forEach((keyEl) => {
            const vkCode = parseInt(keyEl.getAttribute('data-vk'), 16);
            if (isNaN(vkCode)) return;

            keyEl.addEventListener('pointerdown', (e) => {
                e.preventDefault();
                this.activeKeys.set(e.pointerId, { keyEl, vkCode });
                keyEl.classList.add('active');
                this.sendKeyboardKey(vkCode, true);
                this.triggerHaptic(10);
            });
        });

        // Window-level release ensures that moving off the key or multi-touch release is 100% caught
        const handleGlobalKeyUp = (e) => {
            if (this.activeKeys.has(e.pointerId)) {
                const item = this.activeKeys.get(e.pointerId);
                this.activeKeys.delete(e.pointerId);
                if (item.keyEl) item.keyEl.classList.remove('active');
                this.sendKeyboardKey(item.vkCode, false);
            }
        };

        window.addEventListener('pointerup', handleGlobalKeyUp);
        window.addEventListener('pointercancel', handleGlobalKeyUp);
        window.addEventListener('blur', () => {
            for (const [, item] of this.activeKeys) {
                if (item.keyEl) item.keyEl.classList.remove('active');
                this.sendKeyboardKey(item.vkCode, false);
            }
            this.activeKeys.clear();
        });
    }

    triggerHaptic(ms) {
        if (window.OmniPadNative && typeof window.OmniPadNative.vibrate === 'function') {
            try { window.OmniPadNative.vibrate(ms); return; } catch (_) {}
        }
        if ('vibrate' in navigator) {
            try { navigator.vibrate(ms); } catch (_) {}
        }
    }

    // =========================================================================
    // Protocol Dispatches to PCServer / Bluetooth HID
    // =========================================================================
    sendMouseMove(dx, dy) {
        if (this.network && this.network.socket && this.network.socket.readyState === WebSocket.OPEN) {
            // Opcode 0x30: MsgMouseMove (DA 01 30 slot dx_i16 dy_i16)
            const buf = new ArrayBuffer(8);
            const v = new DataView(buf);
            v.setUint8(0, 0xDA);
            v.setUint8(1, 0x01);
            v.setUint8(2, 0x30);
            v.setUint8(3, this.network.padSlot || 0);
            v.setInt16(4, dx, true);
            v.setInt16(6, dy, true);
            this.network.socket.send(buf);
        }
    }

    sendMouseButton(buttonMask, isDown) {
        if (this.network && this.network.socket && this.network.socket.readyState === WebSocket.OPEN) {
            // Opcode 0x31: MsgMouseButton (DA 01 31 slot btnMask isDown)
            const buf = new Uint8Array([0xDA, 0x01, 0x31, this.network.padSlot || 0, buttonMask, isDown ? 1 : 0]);
            this.network.socket.send(buf.buffer);
        }
    }

    sendMouseWheel(deltaY) {
        if (this.network && this.network.socket && this.network.socket.readyState === WebSocket.OPEN) {
            // Opcode 0x32: MsgMouseWheel (DA 01 32 slot wheel_i16)
            const buf = new ArrayBuffer(6);
            const v = new DataView(buf);
            v.setUint8(0, 0xDA);
            v.setUint8(1, 0x01);
            v.setUint8(2, 0x32);
            v.setUint8(3, this.network.padSlot || 0);
            v.setInt16(4, deltaY, true);
            this.network.socket.send(buf);
        }
    }

    sendKeyboardKey(vkCode, isDown) {
        if (this.network && this.network.socket && this.network.socket.readyState === WebSocket.OPEN) {
            // Opcode 0x33: MsgKeyboardKey (DA 01 33 slot vkCode_u16 isDown)
            const buf = new ArrayBuffer(7);
            const v = new DataView(buf);
            v.setUint8(0, 0xDA);
            v.setUint8(1, 0x01);
            v.setUint8(2, 0x33);
            v.setUint8(3, this.network.padSlot || 0);
            v.setUint16(4, vkCode, true);
            v.setUint8(6, isDown ? 1 : 0);
            this.network.socket.send(buf);
        }
    }
}
