/**
 * OmniPad Main Application Controller
 * Orchestrates network, touch, gyro, macros, customizer, and UI state.
 */
class OmniPadApp {
    constructor() {
        this.container = document.getElementById('gamepad-container');
        this.presetSelector = document.getElementById('preset-selector');
        const urlParams = new URLSearchParams(window.location.search);
        const presetParam = urlParams.get('preset');
        this.currentPresetKey = (presetParam && LAYOUT_PRESETS[presetParam]) ? presetParam : 'xbox';
        if (this.presetSelector) this.presetSelector.value = this.currentPresetKey;
        this.currentLayout = [];

        // 1. Initialize Engines
        this.network = new NetworkClient();
        this.audio = new AudioEngine();
        this.touch = new TouchEngine((state) => this.network.sendInput(state));
        this.touch.onTouchpadChanged = (tpState) => this.network.sendTouchpad(tpState);
        this.gyro = new GyroEngine(this.touch, this.network);
        this.macros = new MacroEngine(this.touch);
        this.customizer = new LayoutCustomizer(this);

        // Network Callbacks
        this.network.onStatusChange = (text, connected) => {
            const pill = document.getElementById('status-pill');
            const statusText = document.getElementById('status-text');
            if (pill && statusText) {
                pill.className = `status-pill ${connected ? 'connected' : 'disconnected'}`;
                statusText.textContent = text;
            }
        };

        this.network.onRumble = (large, small) => {
            const intensity = Math.max(large, small);
            if (intensity > 0 && this.touch.hapticsEnabled) {
                if (window.OmniPadNative && typeof window.OmniPadNative.vibrateHeavy === 'function') {
                    try {
                        window.OmniPadNative.vibrateHeavy(Math.min(100, Math.round(intensity / 2.5)));
                        return;
                    } catch { }
                }
                try {
                    navigator.vibrate([Math.min(100, Math.round(intensity / 2.5))]);
                } catch { }
            }
        };

        // Slot Management Callbacks
        this.slotStatuses = new Array(16).fill(0);

        this.network.onSlotStatus = (statuses) => {
            this.slotStatuses = statuses;
            this.renderPlayerSlotsList();
        };

        this.network.onSlotChanged = (newSlot) => {
            this.showToast(`Controller Assigned: Player ${newSlot + 1}`);
            this.renderPlayerSlotsList();
            if (this.touch.hapticsEnabled) {
                try { navigator.vibrate([40, 60, 40]); } catch { }
            }
        };

        this.network.onSwapPrompt = (fromSlot) => {
            this.showSwapPromptModal(fromSlot);
        };

        this.network.onSwapDeclined = (targetSlot) => {
            this.showToast(`Player ${targetSlot + 1} declined the swap request.`);
        };

        this.network.onProfileChange = (profileName) => {
            const key = profileName.toLowerCase();
            if (LAYOUT_PRESETS[key] && this.currentPresetKey !== key) {
                this.showToast(`Auto-Profile: ${key.toUpperCase()}`);
                this.currentPresetKey = key;
                if (this.presetSelector) this.presetSelector.value = key;
                this.loadProfile(key);
            }
        };

        // 2. Setup Lifecycle & Event Listeners
        this.initUi();
        this.updateDynamicScale();
        this.loadProfile(this.currentPresetKey);
        this.network.connect();
        this.requestWakeLock();

        // Responsive auto-scaling on window resize & orientation changes
        window.addEventListener('resize', () => this.updateDynamicScale());
        window.addEventListener('orientationchange', () => {
            setTimeout(() => this.updateDynamicScale(), 150);
        });

        // Screen wake lock & instant reconnect on browser tab resume
        document.addEventListener('visibilitychange', () => {
            if (document.visibilityState === 'visible') {
                this.requestWakeLock();
                if (!this.network.isConnected || !this.network.socket || this.network.socket.readyState !== WebSocket.OPEN) {
                    this.network.reconnectImmediately();
                }
            }
        });

        // Re-request wake lock on user touch gesture if previously denied/released
        window.addEventListener('pointerdown', () => {
            if (!this.wakeLock) {
                this.requestWakeLock();
            }
        }, { passive: true });
    }

    getResponsiveScale() {
        const w = window.innerWidth || document.documentElement.clientWidth;
        const h = window.innerHeight || document.documentElement.clientHeight;
        
        // Base reference dimensions: 844 x 390 (modern landscape phone)
        const scaleH = h / 390;
        const scaleW = w / 844;
        
        // Compute adaptive fit
        let autoScale = Math.min(scaleH, scaleW * 1.05);
        autoScale = Math.max(0.65, Math.min(1.6, autoScale));

        const userScale = parseFloat(localStorage.getItem('omnipad_user_scale') || '1.0');
        return autoScale * userScale;
    }

    updateDynamicScale() {
        const scale = this.getResponsiveScale();
        if (this.container) {
            this.container.style.setProperty('--dyn-scale', scale.toFixed(3));
        }
    }

    async requestWakeLock() {
        if ('wakeLock' in navigator) {
            try {
                this.wakeLock = await navigator.wakeLock.request('screen');
            } catch { }
        }
    }

    initUi() {
        // Floating HUD pill toggling Top Navigation Drawer
        const topBar = document.getElementById('top-bar');
        const statusPill = document.getElementById('status-pill');
        const btnHideTopbar = document.getElementById('btn-hide-topbar');

        const toggleTopBar = (e) => {
            if (e) e.stopPropagation();
            if (topBar) {
                const isCollapsed = topBar.classList.toggle('collapsed');
                document.body.classList.toggle('toolbar-open', !isCollapsed);
            }
        };

        const hideTopBar = () => {
            if (topBar && !topBar.classList.contains('collapsed')) {
                topBar.classList.add('collapsed');
                document.body.classList.remove('toolbar-open');
            }
        };

        statusPill?.addEventListener('click', toggleTopBar);
        btnHideTopbar?.addEventListener('click', hideTopBar);

        // Player Switcher Modal Openers
        const slotBadge = document.getElementById('slot-badge');
        const openSlotModal = (e) => {
            if (e) {
                e.stopPropagation();
                e.preventDefault();
            }
            this.openPlayerSwitchModal();
        };

        slotBadge?.addEventListener('pointerdown', (e) => e.stopPropagation());
        slotBadge?.addEventListener('click', openSlotModal);

        document.getElementById('btn-topbar-slot')?.addEventListener('click', (e) => {
            e.stopPropagation();
            hideTopBar();
            this.openPlayerSwitchModal();
        });

        // Close modal buttons and backdrop clicks
        document.getElementById('btn-close-player-switch')?.addEventListener('click', (e) => {
            e.stopPropagation();
            this.closePlayerSwitchModal();
        });
        document.getElementById('backdrop-player-switch')?.addEventListener('click', (e) => {
            e.stopPropagation();
            this.closePlayerSwitchModal();
        });

        // Headphone Jack (Audio Streaming) Button & Callback
        const btnAudioJack = document.getElementById('btn-audio-jack');
        const audioJackText = document.getElementById('audio-jack-text');

        btnAudioJack?.addEventListener('click', (e) => {
            e.stopPropagation();
            this.audio.toggle();
        });

        this.audio.onStateChange = (streaming) => {
            if (btnAudioJack) {
                btnAudioJack.classList.toggle('active', streaming);
            }
            if (audioJackText) {
                audioJackText.textContent = streaming ? 'Jack: ON' : 'Jack: OFF';
            }
            if (streaming) {
                this.showToast('🎧 Headphone Jack Connected (Game Audio Live)');
            } else {
                this.showToast('🎧 Headphone Jack Disconnected');
            }
        };

        // Clicking / touching anywhere on the gamepad container collapses the top drawer during play
        this.container?.addEventListener('pointerdown', () => {
            if (topBar && !topBar.classList.contains('collapsed') && !this.customizer.isEditing) {
                hideTopBar();
            }
        });

        // Preset Switching
        this.presetSelector?.addEventListener('change', (e) => {
            this.currentPresetKey = e.target.value;
            this.loadProfile(this.currentPresetKey);
        });

        // Fullscreen Toggle
        document.getElementById('btn-fullscreen')?.addEventListener('click', () => {
            if (!document.fullscreenElement) {
                document.documentElement.requestFullscreen().catch(() => { });
            } else {
                document.exitFullscreen().catch(() => { });
            }
        });

        // Settings Modal
        const settingsModal = document.getElementById('settings-modal');
        document.getElementById('btn-settings')?.addEventListener('click', () => {
            hideTopBar();
            settingsModal?.classList.remove('hidden');
            document.body.classList.add('modal-open');
        });
        document.getElementById('btn-close-settings')?.addEventListener('click', () => {
            settingsModal?.classList.add('hidden');
            document.body.classList.remove('modal-open');
        });

        // Modal Tabs
        document.querySelectorAll('.tab-btn').forEach(btn => {
            btn.addEventListener('click', () => {
                document.querySelectorAll('.tab-btn').forEach(b => b.classList.remove('active'));
                document.querySelectorAll('.tab-pane').forEach(p => p.classList.remove('active'));
                btn.classList.add('active');
                document.getElementById(btn.dataset.tab)?.classList.add('active');
            });
        });

        // Settings Controls
        document.getElementById('setting-theme')?.addEventListener('change', (e) => {
            document.body.className = e.target.value;
        });

        // Controller Global Scale Slider
        const scaleSlider = document.getElementById('setting-controller-scale');
        const scaleVal = document.getElementById('val-controller-scale');
        const savedScale = localStorage.getItem('omnipad_user_scale') || '1.0';
        if (scaleSlider && scaleVal) {
            const pct = Math.round(parseFloat(savedScale) * 100);
            scaleSlider.value = pct;
            scaleVal.textContent = `${pct}%`;
            scaleSlider.addEventListener('input', (e) => {
                const val = e.target.value;
                scaleVal.textContent = `${val}%`;
                localStorage.setItem('omnipad_user_scale', (val / 100).toString());
                this.updateDynamicScale();
            });
        }

        document.getElementById('setting-deadzone')?.addEventListener('input', (e) => {
            const val = e.target.value / 100.0;
            this.touch.deadzone = val;
            document.getElementById('val-deadzone').textContent = `${e.target.value}%`;
        });

        document.getElementById('setting-floating-stick')?.addEventListener('change', (e) => {
            this.touch.floatingSticks = e.target.checked;
        });

        document.getElementById('setting-touch-haptics')?.addEventListener('change', (e) => {
            this.touch.hapticsEnabled = e.target.checked;
        });

        document.getElementById('setting-brightness')?.addEventListener('input', (e) => {
            const pct = e.target.value;
            document.getElementById('val-brightness').textContent = `${pct}%`;
            document.body.style.filter = `brightness(${pct}%)`;
        });

        // Headphone Jack Volume
        const audioVolumeSlider = document.getElementById('setting-audio-volume');
        const audioVolumeVal = document.getElementById('val-audio-volume');
        if (audioVolumeSlider && audioVolumeVal) {
            audioVolumeSlider.addEventListener('input', (e) => {
                const val = parseInt(e.target.value, 10);
                audioVolumeVal.textContent = `${val}%`;
                this.audio.setVolume(val / 100.0);
            });
        }

        // Gyro Settings
        document.getElementById('setting-gyro-mode')?.addEventListener('change', (e) => {
            this.gyro.mode = e.target.value;
        });

        document.getElementById('setting-gyro-touch-only')?.addEventListener('change', (e) => {
            this.gyro.touchOnly = e.target.checked;
        });

        document.getElementById('btn-request-gyro')?.addEventListener('click', async () => {
            const granted = await this.gyro.init();
            alert(granted ? 'Motion sensors initialized!' : 'Permission denied or sensors unavailable.');
        });

        // Profile Export/Import
        document.getElementById('btn-export-profile')?.addEventListener('click', () => {
            const dataStr = "data:text/json;charset=utf-8," + encodeURIComponent(JSON.stringify(this.currentLayout, null, 2));
            const dl = document.createElement('a');
            dl.setAttribute("href", dataStr);
            dl.setAttribute("download", `OmniPad_${this.currentPresetKey}.json`);
            dl.click();
        });

        const fileInput = document.getElementById('file-import-profile');
        document.getElementById('btn-import-profile')?.addEventListener('click', () => fileInput.click());
        fileInput?.addEventListener('change', (e) => {
            const file = e.target.files[0];
            if (file) {
                const reader = new FileReader();
                reader.onload = (event) => {
                    try {
                        this.currentLayout = JSON.parse(event.target.result);
                        this.saveCurrentProfile();
                        this.renderLayout();
                        alert('Profile imported successfully!');
                    } catch {
                        alert('Invalid profile JSON file.');
                    }
                };
                reader.readAsText(file);
            }
        });
    }

    loadProfile(key) {
        // Enforce cache invalidation for upgraded ergonomic presets
        const PRESET_VERSION = 'v7_touchpad_switch_pro';
        if (localStorage.getItem('omnipad_version') !== PRESET_VERSION) {
            for (let k of Object.keys(LAYOUT_PRESETS)) {
                localStorage.removeItem(`omnipad_layout_${k}`);
            }
            localStorage.setItem('omnipad_version', PRESET_VERSION);
        }

        const saved = localStorage.getItem(`omnipad_layout_${key}`);
        if (saved) {
            try {
                this.currentLayout = JSON.parse(saved);
                this.renderLayout();
                return;
            } catch { }
        }

        // Load factory preset
        this.currentLayout = JSON.parse(JSON.stringify(LAYOUT_PRESETS[key] || LAYOUT_PRESETS.xbox));
        this.renderLayout();
    }

    saveCurrentProfile() {
        localStorage.setItem(`omnipad_layout_${this.currentPresetKey}`, JSON.stringify(this.currentLayout));
    }

    resetCurrentProfile() {
        localStorage.removeItem(`omnipad_layout_${this.currentPresetKey}`);
        this.loadProfile(this.currentPresetKey);
    }

    renderLayout() {
        this.container.innerHTML = '';

        this.currentLayout.forEach(item => {
            const el = document.createElement('div');
            el.className = 'control-elem';
            el.id = item.id;
            el.style.left = `${item.x * 100}%`;
            el.style.top = `${item.y * 100}%`;
            el.style.width = `${item.w}px`;
            el.style.height = `${item.h}px`;

            if (item.scale && item.scale !== 1.0) {
                el.style.setProperty('--elem-scale', item.scale.toString());
            } else {
                el.style.removeProperty('--elem-scale');
            }

            if (item.btnClass) {
                item.btnClass.trim().split(/\s+/).forEach(cls => {
                    if (cls) el.classList.add(cls);
                });
            }

            // 1. Joystick
            if (item.type === 'joystick') {
                el.classList.add('joystick-base');
                const knob = document.createElement('div');
                knob.className = 'joystick-knob';
                el.appendChild(knob);
                this.touch.bindControlElement(el, 'joystick', item.binding);
            }
            // 2. Standard Button (Face, Shoulder, System, Pill, Key, Action)
            else if (item.type === 'button') {
                if (item.shape === 'shoulder') {
                    el.classList.add('shoulder-btn');
                    el.textContent = item.label || '';
                } else if (item.shape === 'pill') {
                    el.classList.add('pill-btn');
                    el.textContent = item.label || '';
                } else if (item.shape === 'key') {
                    el.classList.add('key-btn');
                    el.textContent = item.label || '';
                } else if (item.shape === 'action') {
                    el.classList.add('game-btn', 'action-btn');
                    el.textContent = item.label || '';
                } else if (item.svg) {
                    el.classList.add('sys-btn');
                    if (item.svg === 'view') {
                        el.innerHTML = `<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="3" y="6" width="11" height="11" rx="1.5"/><rect x="10" y="10" width="11" height="11" rx="1.5"/></svg>`;
                    } else if (item.svg === 'menu') {
                        el.innerHTML = `<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><line x1="4" y1="7" x2="20" y2="7"/><line x1="4" y1="12" x2="20" y2="12"/><line x1="4" y1="17" x2="20" y2="17"/></svg>`;
                    } else if (item.svg === 'guide') {
                        el.innerHTML = `<svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="10"/><path d="M7 7c3 3 7 3 10 0"/><path d="M7 17c3-3 7-3 10 0"/><circle cx="12" cy="12" r="2.5" fill="currentColor"/></svg>`;
                    }
                    el.title = item.label || '';
                } else {
                    el.classList.add('game-btn');
                    if (item.label && item.label.length > 2) {
                        el.classList.add('action-btn');
                    }
                    el.textContent = item.label || '';
                }
                this.touch.bindControlElement(el, 'button', item.binding);
            }
            // 3. Trigger
            else if (item.type === 'trigger') {
                if (item.shape === 'action') {
                    el.classList.add('game-btn', 'action-btn');
                    el.textContent = item.label || item.binding;
                } else {
                    el.classList.add('trigger-slider-container');
                    if (item.shape === 'shoulder') {
                        el.classList.add('shoulder-btn');
                    }
                    const fill = document.createElement('div');
                    fill.className = 'trigger-fill';
                    const lbl = document.createElement('span');
                    lbl.className = 'trigger-label';
                    lbl.textContent = item.label || item.binding;
                    el.appendChild(fill);
                    el.appendChild(lbl);
                }
                this.touch.bindControlElement(el, 'trigger', item.binding);
            }
            // 4. Tactile Embossed D-Pad Cross (Diagonal tracking + Crisp SVGs)
            else if (item.type === 'dpad') {
                el.classList.add('dpad-container');
                if (item.spacing !== undefined) {
                    el.style.setProperty('--dpad-gap', `${item.spacing}px`);
                }
                if (item.btnScale !== undefined) {
                    el.style.setProperty('--dpad-btn-scale', item.btnScale.toString());
                }
                if (item.showBg === false) {
                    el.classList.add('dpad-no-bg');
                }
                el.innerHTML = `
                    <div class="dpad-cross">
                        <div class="dpad-btn dpad-up" title="Up">
                            <svg viewBox="0 0 24 24"><path d="M12 4l-7 8h4v8h6v-8h4z"/></svg>
                        </div>
                        <div class="dpad-btn dpad-left" title="Left">
                            <svg viewBox="0 0 24 24"><path d="M4 12l8-7v4h8v6h-8v4z"/></svg>
                        </div>
                        <div class="dpad-btn dpad-right" title="Right">
                            <svg viewBox="0 0 24 24"><path d="M20 12l-8-7v4H4v6h8v4z"/></svg>
                        </div>
                        <div class="dpad-btn dpad-down" title="Down">
                            <svg viewBox="0 0 24 24"><path d="M12 20l7-8h-4V4H9v8H5z"/></svg>
                        </div>
                        <div class="dpad-core"></div>
                    </div>
                `;
                this.touch.bindDpadElement(el);
            }
            // 5. D-Pad 8-Way Matrix (Fighting Hitbox)
            else if (item.type === 'dpad_matrix') {
                el.classList.add('dpad-grid');
                const cells = [
                    { dir: 'UL', mask: 0x0001 | 0x0004, svg: `<svg viewBox="0 0 24 24" width="20" height="20"><path fill="currentColor" d="M7 17V7h10v2.5H9.5V17z"/></svg>` },
                    { dir: 'U', mask: 0x0001, svg: `<svg viewBox="0 0 24 24" width="20" height="20"><path fill="currentColor" d="M12 4l-6 6h4v10h4V10h4z"/></svg>` },
                    { dir: 'UR', mask: 0x0001 | 0x0008, svg: `<svg viewBox="0 0 24 24" width="20" height="20"><path fill="currentColor" d="M17 17V7H7v2.5h7.5V17z"/></svg>` },
                    { dir: 'L', mask: 0x0004, svg: `<svg viewBox="0 0 24 24" width="20" height="20"><path fill="currentColor" d="M4 12l6-6v4h10v4H10v4z"/></svg>` },
                    { dir: 'C', mask: 0, svg: `<span style="font-size: 14px; opacity: 0.4;">●</span>` },
                    { dir: 'R', mask: 0x0008, svg: `<svg viewBox="0 0 24 24" width="20" height="20"><path fill="currentColor" d="M20 12l-6-6v4H4v4h10v4z"/></svg>` },
                    { dir: 'DL', mask: 0x0002 | 0x0004, svg: `<svg viewBox="0 0 24 24" width="20" height="20"><path fill="currentColor" d="M7 7v10h10v-2.5H9.5V7z"/></svg>` },
                    { dir: 'D', mask: 0x0002, svg: `<svg viewBox="0 0 24 24" width="20" height="20"><path fill="currentColor" d="M12 20l6-6h-4V4h-4v10H6z"/></svg>` },
                    { dir: 'DR', mask: 0x0002 | 0x0008, svg: `<svg viewBox="0 0 24 24" width="20" height="20"><path fill="currentColor" d="M17 7v10H7v-2.5h7.5V7z"/></svg>` }
                ];
                cells.forEach(cell => {
                    const c = document.createElement('div');
                    c.className = `dpad-cell ${cell.dir === 'C' ? 'dpad-center' : ''}`;
                    c.innerHTML = cell.svg;
                    if (cell.mask !== 0) {
                        this.touch.bindControlElement(c, 'button', cell.mask);
                    }
                    el.appendChild(c);
                });
            }
            // 6. Racing Pedals
            else if (item.type === 'pedal') {
                el.classList.add('pedal-box', item.binding === 'RT' ? 'pedal-gas' : 'pedal-brake');
                const fill = document.createElement('div');
                fill.className = 'pedal-fill';
                const lbl = document.createElement('span');
                lbl.className = 'trigger-label';
                lbl.textContent = item.label;
                el.appendChild(fill);
                el.appendChild(lbl);
                this.touch.bindControlElement(el, 'trigger', item.binding);
            }
            // 7. Steering Wheel Indicator (Gyro / Touch Wheel)
            else if (item.type === 'wheel_indicator') {
                el.classList.add('joystick-base');
                el.innerHTML = `
                    <svg viewBox="0 0 100 100" width="100%" height="100%" style="opacity: 0.7; stroke: var(--accent); fill: none; stroke-width: 4;">
                        <circle cx="50" cy="50" r="42" stroke-dasharray="6,4"/>
                        <circle cx="50" cy="50" r="16"/>
                        <line x1="8" y1="50" x2="34" y2="50" stroke-width="6"/>
                        <line x1="66" y1="50" x2="92" y2="50" stroke-width="6"/>
                        <line x1="50" y1="66" x2="50" y2="92" stroke-width="6"/>
                    </svg>
                `;
            }
            // 8. PS4/PS5 Touchpad & Trackpad
            else if (item.type === 'touchpad') {
                el.classList.add('touchpad-surface');
                el.innerHTML = `
                    <div class="touchpad-bar"></div>
                    <div class="touchpad-label">${item.label || 'TOUCHPAD'}</div>
                `;
                this.touch.bindTouchpadElement(el);
            }
            // 8b. Trackpad Mouse
            else if (item.type === 'trackpad_mouse') {
                el.classList.add('trackpad-surface');
                this.touch.bindTouchpadElement(el);
            }
            // 9. FPS Aim Trackpad
            else if (item.type === 'trackpad_aim') {
                el.classList.add('trackpad-surface');
                let lastX, lastY;
                el.addEventListener('pointerdown', (e) => {
                    lastX = e.clientX;
                    lastY = e.clientY;
                    this.gyro.setAimingTouchActive(true);
                });
                el.addEventListener('pointermove', (e) => {
                    if (lastX !== undefined) {
                        const dx = (e.clientX - lastX) * 280;
                        const dy = -(e.clientY - lastY) * 280;
                        lastX = e.clientX;
                        lastY = e.clientY;
                        this.touch.setStick('right', dx / 32767, dy / 32767);
                    }
                });
                el.addEventListener('pointerup', () => {
                    lastX = undefined;
                    this.gyro.setAimingTouchActive(false);
                    this.touch.setStick('right', 0, 0);
                });
            }
            // 10. Combo Button
            else if (item.type === 'combo') {
                el.classList.add('game-btn');
                if (item.shape === 'action' || (item.label && item.label.length > 2)) {
                    el.classList.add('action-btn');
                }
                el.textContent = item.label;
                el.addEventListener('pointerdown', (e) => {
                    e.preventDefault();
                    this.touch.triggerHaptic(20);
                    el.classList.add('active');
                    this.macros.triggerCombo(item.combo, true);
                });
                el.addEventListener('pointerup', () => {
                    el.classList.remove('active');
                    this.macros.triggerCombo(item.combo, false);
                });
            }
            // 11. Macro Button
            else if (item.type === 'macro') {
                el.classList.add(item.shape === 'pill' ? 'pill-btn' : 'game-btn');
                if (item.shape !== 'pill' && (item.shape === 'action' || (item.label && item.label.length > 2))) {
                    el.classList.add('action-btn');
                }
                el.textContent = item.label;
                el.addEventListener('pointerdown', (e) => {
                    e.preventDefault();
                    this.touch.triggerHaptic(25);
                    el.classList.add('active');
                    this.macros.executeMacro(item.macroId, false);
                });
                el.addEventListener('pointerup', () => {
                    el.classList.remove('active');
                    this.macros.cancelActiveMacro();
                });
            }

            // Enable customizer drag handling in Edit mode
            this.customizer.makeDraggable(el, item);
            this.container.appendChild(el);
        });

        this.updateDynamicScale();
    }

    openPlayerSwitchModal() {
        const modal = document.getElementById('modal-player-switch');
        if (modal) {
            const topBar = document.getElementById('top-bar');
            if (topBar && !topBar.classList.contains('collapsed')) {
                topBar.classList.add('collapsed');
                document.body.classList.remove('toolbar-open');
            }
            this.renderPlayerSlotsList();
            this.setupModalScrollHandling();
            modal.classList.remove('hidden');
            document.body.classList.add('modal-open');
        }
    }

    closePlayerSwitchModal() {
        const modal = document.getElementById('modal-player-switch');
        if (modal) {
            modal.classList.add('hidden');
            const swapPrompt = document.getElementById('modal-swap-prompt');
            const settings = document.getElementById('settings-modal');
            if ((!swapPrompt || swapPrompt.classList.contains('hidden')) &&
                (!settings || settings.classList.contains('hidden'))) {
                document.body.classList.remove('modal-open');
            }
        }
    }

    setupModalScrollHandling() {
        const scrollBody = document.querySelector('.player-switch-body');
        if (!scrollBody || scrollBody._scrollBound) return;
        scrollBody._scrollBound = true;

        let startY = 0;
        let startScrollTop = 0;
        let lastY = 0;
        let lastTime = 0;
        let velocity = 0;
        let isTouching = false;
        let animId = null;

        scrollBody.addEventListener('touchstart', (e) => {
            if (e.touches.length !== 1) return;
            if (animId) cancelAnimationFrame(animId);
            isTouching = true;
            startY = e.touches[0].clientY;
            startScrollTop = scrollBody.scrollTop;
            lastY = startY;
            lastTime = Date.now();
            velocity = 0;
        }, { passive: true });

        scrollBody.addEventListener('touchmove', (e) => {
            if (!isTouching || e.touches.length !== 1) return;
            const currentY = e.touches[0].clientY;
            const now = Date.now();
            const dt = now - lastTime;
            if (dt > 0) {
                velocity = (lastY - currentY) / dt;
            }
            lastY = currentY;
            lastTime = now;

            const dy = currentY - startY;
            scrollBody.scrollTop = startScrollTop - dy;
        }, { passive: true });

        const endTouch = () => {
            if (!isTouching) return;
            isTouching = false;
            if (Math.abs(velocity) > 0.15) {
                let currentVelocity = velocity * 14;
                const stepInertia = () => {
                    if (Math.abs(currentVelocity) < 0.5) return;
                    scrollBody.scrollTop += currentVelocity;
                    currentVelocity *= 0.92;
                    animId = requestAnimationFrame(stepInertia);
                };
                animId = requestAnimationFrame(stepInertia);
            }
        };

        scrollBody.addEventListener('touchend', endTouch, { passive: true });
        scrollBody.addEventListener('touchcancel', endTouch, { passive: true });
    }

    renderPlayerSlotsList() {
        const container = document.getElementById('player-slots-container');
        if (!container) return;

        container.innerHTML = '';
        const mySlot = this.network.padSlot;
        const totalSlots = Math.max(16, (this.slotStatuses && this.slotStatuses.length) ? this.slotStatuses.length : 16);

        // Non-blocking action trigger: differentiates between swipe-scrolling and intentional tap
        const bindSlotAction = (btn, action) => {
            let startX = 0;
            let startY = 0;
            let hasMoved = false;

            btn.addEventListener('pointerdown', (e) => {
                startX = e.clientX;
                startY = e.clientY;
                hasMoved = false;
            });

            btn.addEventListener('pointermove', (e) => {
                if (Math.abs(e.clientX - startX) > 8 || Math.abs(e.clientY - startY) > 8) {
                    hasMoved = true;
                }
            });

            btn.addEventListener('pointerup', (e) => {
                if (!hasMoved) {
                    e.stopPropagation();
                    if (this.touch.hapticsEnabled) {
                        try { navigator.vibrate(14); } catch { }
                    }
                    action();
                }
            });

            btn.addEventListener('click', (e) => {
                e.stopPropagation();
            });
        };

        const createTierHeader = (title, badge) => {
            const tier = document.createElement('div');
            tier.className = 'slot-tier-header';
            tier.innerHTML = `<span class="slot-tier-title">${title}</span><span class="slot-tier-badge">${badge}</span>`;
            return tier;
        };

        for (let i = 0; i < totalSlots; i++) {
            // Tier Section Headers
            if (i === 0) {
                container.appendChild(createTierHeader('P1 – P4: Standard Co-op', 'Universal (XInput/WGI/SDL2)'));
            } else if (i === 4) {
                container.appendChild(createTierHeader('P5 – P8: Extended Party', 'WGI / DirectInput / SDL2'));
            } else if (i === 8) {
                container.appendChild(createTierHeader('P9 – P16: Mega-Party', 'WGI / SDL2 / Emulators'));
            }

            const isMe = (i === mySlot);
            const isOccupied = (this.slotStatuses && this.slotStatuses[i] === 1);

            const card = document.createElement('div');
            card.className = `player-slot-card ${isMe ? 'active-user' : (isOccupied ? 'occupied' : '')}`;

            const header = document.createElement('div');
            header.className = 'slot-card-header';

            const identity = document.createElement('div');
            identity.className = 'slot-card-identity';

            const pill = document.createElement('div');
            pill.className = 'slot-number-pill';
            pill.textContent = `P${i + 1}`;

            const name = document.createElement('span');
            name.className = 'slot-card-name';
            name.textContent = `Player ${i + 1}`;

            identity.appendChild(pill);
            identity.appendChild(name);

            const statusPill = document.createElement('span');
            statusPill.className = 'slot-status-pill';
            statusPill.textContent = isMe ? 'Active' : (isOccupied ? 'In Use' : 'Open');

            header.appendChild(identity);
            header.appendChild(statusPill);
            card.appendChild(header);

            const actionBtn = document.createElement('button');
            actionBtn.className = 'btn-slot-card-action';

            if (isMe) {
                actionBtn.classList.add('btn-active-indicator');
                actionBtn.innerHTML = `
                    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3"><polyline points="20 6 9 17 4 12"/></svg>
                    <span>Current Controller</span>
                `;
            } else if (isOccupied) {
                actionBtn.classList.add('btn-swap-action');
                actionBtn.innerHTML = `
                    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><polyline points="16 3 21 3 21 8"></polyline><line x1="4" y1="20" x2="21" y2="3"></line><polyline points="21 16 21 21 16 21"></polyline><line x1="15" y1="15" x2="21" y2="21"></line><line x1="4" y1="4" x2="9" y2="9"></line></svg>
                    <span>Request Swap</span>
                `;
                bindSlotAction(actionBtn, () => {
                    this.network.requestSlotSwap(i);
                    this.showToast(`Swap request sent to Player ${i + 1}...`);
                    this.closePlayerSwitchModal();
                });
            } else {
                actionBtn.classList.add('btn-join-action');
                actionBtn.innerHTML = `
                    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><line x1="5" y1="12" x2="19" y2="12"></line><polyline points="12 5 19 12 12 19"></polyline></svg>
                    <span>Switch to P${i + 1}</span>
                `;
                bindSlotAction(actionBtn, () => {
                    this.network.requestSlotSwitch(i);
                    this.closePlayerSwitchModal();
                });
            }

            card.appendChild(actionBtn);
            container.appendChild(card);
        }
    }

    showSwapPromptModal(fromSlot) {
        this.currentSwapRequester = fromSlot;
        const modal = document.getElementById('modal-swap-prompt');
        const desc = document.getElementById('swap-prompt-desc');
        const timerBar = document.getElementById('swap-timer-bar');
        const timerSec = document.getElementById('swap-timer-sec');
        const btnAccept = document.getElementById('btn-accept-swap');
        const btnDecline = document.getElementById('btn-decline-swap');
        const backdrop = document.getElementById('backdrop-swap-prompt');

        if (!modal) return;

        // Hide top drawer if open
        const topBar = document.getElementById('top-bar');
        if (topBar && !topBar.classList.contains('collapsed')) {
            topBar.classList.add('collapsed');
            document.body.classList.remove('toolbar-open');
        }

        if (desc) desc.textContent = `Player ${fromSlot + 1} wants to swap controller slots with you.`;

        // Trigger vibration alert
        try { navigator.vibrate([100, 50, 100]); } catch { }

        modal.classList.remove('hidden');
        document.body.classList.add('modal-open');

        // 15-second countdown timer
        const totalDuration = 15000;
        const startTime = Date.now();
        if (timerBar) timerBar.style.width = '100%';
        if (timerSec) timerSec.textContent = '15s';

        if (this.swapCountdownInterval) clearInterval(this.swapCountdownInterval);

        const cleanup = () => {
            if (this.swapCountdownInterval) clearInterval(this.swapCountdownInterval);
            this.swapCountdownInterval = null;
            modal.classList.add('hidden');
            const playerSwitch = document.getElementById('modal-player-switch');
            const settings = document.getElementById('settings-modal');
            if ((!playerSwitch || playerSwitch.classList.contains('hidden')) &&
                (!settings || settings.classList.contains('hidden'))) {
                document.body.classList.remove('modal-open');
            }
        };

        this.swapCountdownInterval = setInterval(() => {
            const elapsed = Date.now() - startTime;
            const remainingRatio = Math.max(0, 1 - (elapsed / totalDuration));
            const remainingSec = Math.max(0, Math.ceil((totalDuration - elapsed) / 1000));

            if (timerBar) timerBar.style.width = `${(remainingRatio * 100).toFixed(1)}%`;
            if (timerSec) timerSec.textContent = `${remainingSec}s`;

            if (elapsed >= totalDuration) {
                cleanup();
                this.network.respondToSwap(fromSlot, false); // Auto decline on timeout
                this.showToast('Swap request expired.');
            }
        }, 100);

        let answered = false;
        const handleChoice = (accepted) => {
            if (answered) return;
            answered = true;
            cleanup();
            this.network.respondToSwap(fromSlot, accepted);
            if (accepted) {
                this.showToast('Swap accepted! Switching slots...');
            }
        };

        if (btnAccept) {
            btnAccept.onclick = (e) => {
                e.stopPropagation();
                handleChoice(true);
            };
        }
        if (btnDecline) {
            btnDecline.onclick = (e) => {
                e.stopPropagation();
                handleChoice(false);
            };
        }
        if (backdrop) {
            backdrop.onclick = (e) => {
                e.stopPropagation();
                handleChoice(false);
            };
        }
    }

    showToast(message) {
        const toast = document.getElementById('hud-toast');
        if (!toast) return;

        toast.textContent = message;
        toast.classList.remove('hidden');

        if (this.toastTimeout) clearTimeout(this.toastTimeout);
        this.toastTimeout = setTimeout(() => {
            toast.classList.add('hidden');
        }, 3200);
    }
}

// Boot application on DOMContentLoaded
window.addEventListener('DOMContentLoaded', () => {
    window.omnipadApp = new OmniPadApp();
});

// Global bridge function for hardware buttons (e.g. Android Volume keys for Bumpers)
window.omniPadTriggerButton = function(btnName, pressed) {
    if (window.omnipadApp && window.omnipadApp.touch) {
        const mask = window.omnipadApp.touch.BUTTONS[btnName];
        if (mask !== undefined) {
            if (pressed) {
                window.omnipadApp.touch.state.buttons |= mask;
            } else {
                window.omnipadApp.touch.state.buttons &= ~mask;
            }
            window.omnipadApp.touch.emitState();
        }
    }
};

