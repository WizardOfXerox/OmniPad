/**
 * OmniPad Universal Custom Gaming Select & Picker Component
 * Replaces native OS select dialogs (Android 7 legacy radio popups, ColorOS bottom sheets)
 * with a unified, high-performance, dark-themed gaming modal across all devices.
 * Uses 100% Vector SVGs for crisp, professional rendering.
 */

(function () {
    'use strict';

    // Helper to generate consistent inline SVG elements
    function createSvg(pathHtml, viewBox = '0 0 24 24', width = 18, height = 18, strokeColor = 'currentColor') {
        return `<svg width="${width}" height="${height}" viewBox="${viewBox}" fill="none" stroke="${strokeColor}" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">${pathHtml}</svg>`;
    }

    // High-precision Vector SVGs for every controller and control type
    const SVG_ICONS = {
        gamepad: createSvg('<rect x="2" y="6" width="20" height="12" rx="6"></rect><line x1="6" y1="12" x2="10" y2="12"></line><line x1="8" y1="10" x2="8" y2="14"></line><line x1="15" y1="13" x2="15.01" y2="13"></line><line x1="18" y1="11" x2="18.01" y2="11"></line>'),
        arcade: createSvg('<rect x="2" y="7" width="20" height="14" rx="3"></rect><circle cx="15" cy="12" r="1.5"></circle><circle cx="18" cy="12" r="1.5"></circle><circle cx="15" cy="16" r="1.5"></circle><circle cx="18" cy="16" r="1.5"></circle><circle cx="7" cy="11" r="2"></circle><line x1="7" y1="13" x2="7" y2="17"></line>'),
        dpad: createSvg('<path d="M9 3h6v6h6v6h-6v6H9v-6H3V9h6z"></path>'),
        racing: createSvg('<circle cx="12" cy="12" r="10"></circle><circle cx="12" cy="12" r="3"></circle><line x1="12" y1="2" x2="12" y2="9"></line><line x1="2" y1="12" x2="9" y2="12"></line><line x1="15" y1="12" x2="22" y2="12"></line>'),
        flight: createSvg('<path d="M17.8 19.2 16 11l3.5-3.5C21 6 21.5 4 21 3c-1-.5-3 0-4.5 1.5L13 8 4.8 6.2c-.5-.1-.9.1-1.1.5l-.3.5c-.2.5-.1 1 .3 1.3L9 12l-2 3H4l-1 1 3 2 2 3 1-1v-3l3-2 3.5 5.3c.3.4.8.5 1.3.3l.5-.2c.4-.3.6-.7.5-1.2z"></path>'),
        touchpad: createSvg('<rect x="5" y="2" width="14" height="20" rx="7"></rect><line x1="12" y1="6" x2="12" y2="10"></line>'),
        crosshair: createSvg('<circle cx="12" cy="12" r="10"></circle><line x1="22" y1="12" x2="18" y2="12"></line><line x1="6" y1="12" x2="2" y2="12"></line><line x1="12" y1="6" x2="12" y2="2"></line><line x1="12" y1="22" x2="12" y2="18"></line>'),
        handheld: createSvg('<rect x="5" y="2" width="14" height="20" rx="3"></rect><line x1="5" y1="12" x2="19" y2="12"></line><circle cx="9" cy="7" r="1.5"></circle><circle cx="15" cy="7" r="1.5"></circle>'),
        action: createSvg('<polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2"></polygon>'),
        tools: createSvg('<path d="M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.77-3.77a6 6 0 0 1-7.94 7.94l-6.91 6.91a2.12 2.12 0 0 1-3-3l6.91-6.91a6 6 0 0 1 7.94-7.94l-3.76 3.76z"></path>'),
        keyboard: createSvg('<rect x="2" y="4" width="20" height="16" rx="2"></rect><line x1="6" y1="8" x2="6.01" y2="8"></line><line x1="10" y1="8" x2="10.01" y2="8"></line><line x1="14" y1="8" x2="14.01" y2="8"></line><line x1="18" y1="8" x2="18.01" y2="8"></line><line x1="6" y1="12" x2="6.01" y2="12"></line><line x1="18" y1="12" x2="18.01" y2="12"></line><line x1="10" y1="16" x2="14" y2="16"></line>'),
        bolt: createSvg('<polygon points="13 2 3 14 12 14 11 22 21 10 12 10 13 2"></polygon>'),
        usb: createSvg('<rect x="8" y="2" width="8" height="6" rx="1"></rect><path d="M7 8h10v6a5 5 0 0 1-10 0V8z"></path><line x1="12" y1="19" x2="12" y2="22"></line><line x1="10" y1="4" x2="10" y2="5"></line><line x1="14" y1="4" x2="14" y2="5"></line>'),
        wifi: createSvg('<path d="M5 12.55a11 11 0 0 1 14.08 0"></path><path d="M1.42 9a16 16 0 0 1 21.16 0"></path><path d="M8.53 16.11a6 6 0 0 1 6.95 0"></path><line x1="12" y1="20" x2="12.01" y2="20"></line>'),
        bluetooth: createSvg('<polyline points="6.5 6.5 17.5 17.5 12 23 12 1 17.5 6.5 6.5 17.5"></polyline>'),
        offline: createSvg('<line x1="1" y1="1" x2="23" y2="23"></line><path d="M16.72 11.06A10.94 10.94 0 0 1 19 12.55"></path><path d="M5 12.55a10.94 10.94 0 0 1 5.17-2.39"></path><path d="M10.71 5.05A16 16 0 0 1 22.58 9"></path><path d="M1.42 9a15.91 15.91 0 0 1 4.7-2.88"></path><path d="M8.53 16.11a6 6 0 0 1 6.95 0"></path><line x1="12" y1="20" x2="12.01" y2="20"></line>'),
        palette: createSvg('<circle cx="13.5" cy="6.5" r=".5" fill="currentColor"></circle><circle cx="17.5" cy="10.5" r=".5" fill="currentColor"></circle><circle cx="8.5" cy="7.5" r=".5" fill="currentColor"></circle><circle cx="6.5" cy="12.5" r=".5" fill="currentColor"></circle><path d="M12 2C6.5 2 2 6.5 2 12s4.5 10 10 10c.926 0 1.648-.746 1.648-1.688 0-.437-.18-.835-.437-1.125-.29-.289-.438-.652-.438-1.125a1.64 1.64 0 0 1 1.668-1.668h1.996c3.051 0 5.555-2.503 5.555-5.554C21.965 6.012 17.461 2 12 2z"></path>'),
        settings: createSvg('<circle cx="12" cy="12" r="3"></circle><path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1 0 2.83 2 2 0 0 1-2.83 0l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-2 2 2 2 0 0 1-2-2v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83 0 2 2 0 0 1 0-2.83l.06-.06a1.65 1.65 0 0 0 .33-1.82 1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1-2-2 2 2 0 0 1 2-2h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 0 1 0-2.83 2 2 0 0 1 2.83 0l.06.06a1.65 1.65 0 0 0 1.82.33H9a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 2-2 2 2 0 0 1 2 2v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 0 2 2 0 0 1 0 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82V9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 2 2 2 2 0 0 1-2 2h-.09a1.65 1.65 0 0 0-1.51 1z"></path>'),
        power_off: createSvg('<path d="M18.36 6.64a9 9 0 1 1-12.73 0"></path><line x1="12" y1="2" x2="12" y2="12"></line>'),
        check: createSvg('<polyline points="20 6 9 17 4 12"></polyline>', '0 0 24 24', 18, 18, 'var(--accent, #00ff88)')
    };

    // Vector Color Swatch for Themes
    function createThemeSwatch(bg, border, accent) {
        return `<svg width="22" height="22" viewBox="0 0 24 24"><circle cx="12" cy="12" r="10" fill="${bg}" stroke="${border}" stroke-width="2.5"/><circle cx="12" cy="12" r="4.5" fill="${accent}"/></svg>`;
    }

    // Option metadata registry mapping options to clean vector SVGs and badges
    const OPTION_METADATA = {
        // --- Controller Presets ---
        'xbox': { svg: SVG_ICONS.gamepad, badge: 'XInput', color: '#107c10', sub: 'Standard Xbox 360 controller layout' },
        'xbox_elite': { svg: SVG_ICONS.gamepad, badge: 'Elite 2', color: '#107c10', sub: 'Xbox Elite Series 2 with P1-P4 rear paddles' },
        'playstation': { svg: SVG_ICONS.gamepad, badge: 'DualShock', color: '#0070d1', sub: 'PS4 layout with multi-touch touchpad' },
        'ps5_dualsense': { svg: SVG_ICONS.gamepad, badge: 'DualSense Pro', color: '#0070d1', sub: 'PS5 layout with center touchpad & mic mute' },
        'switch_pro': { svg: SVG_ICONS.gamepad, badge: 'Switch Pro', color: '#e60012', sub: 'Nintendo layout (+ & - buttons, 6-axis)' },
        'joycon_dual': { svg: SVG_ICONS.handheld, badge: 'Joy-Cons Dual', color: '#e60012', sub: 'Nintendo dual Joy-Cons with SL/SR shoulder grips' },
        'dual_dpad': { svg: SVG_ICONS.dpad, badge: 'Dual Cross', color: '#f59e0b', sub: 'Fighter setup with dual D-pads' },
        'arcade': { svg: SVG_ICONS.arcade, badge: 'Vewlix', color: '#ec4899', sub: 'Japanese 8-button arcade fightstick' },
        'neogeo_arcade': { svg: SVG_ICONS.arcade, badge: 'Neo Geo MVS', color: '#f59e0b', sub: 'Classic 4-button slanted ABCD arcade fightpad' },
        'hitbox': { svg: SVG_ICONS.dpad, badge: 'Hitbox', color: '#8b5cf6', sub: 'All-button precision fighting controller' },
        'sega_saturn': { svg: SVG_ICONS.arcade, badge: 'Saturn 6B', color: '#06b6d4', sub: 'Sega Genesis / Saturn 6-button pad' },
        'gamecube': { svg: SVG_ICONS.gamepad, badge: 'Smash', color: '#a855f7', sub: 'GameCube layout with analog trigger click' },
        'n64': { svg: SVG_ICONS.gamepad, badge: 'N64 3-Prong', color: '#3b82f6', sub: 'Classic N64 C-buttons and Z trigger' },
        'snes_retro': { svg: SVG_ICONS.gamepad, badge: '16-Bit', color: '#6366f1', sub: 'Classic Super Nintendo layout' },
        'ps1_classic': { svg: SVG_ICONS.gamepad, badge: 'PSX Classic', color: '#94a3b8', sub: 'Original PlayStation 1 digital pad' },
        'nes_retro': { svg: SVG_ICONS.gamepad, badge: '8-Bit', color: '#ef4444', sub: 'Retro NES / Game Boy dual button' },
        'ds_3ds': { svg: SVG_ICONS.handheld, badge: 'Dual-Screen', color: '#14b8a6', sub: 'Nintendo DS / 3DS touch layout' },
        'twinstick': { svg: SVG_ICONS.crosshair, badge: 'Twin-Stick', color: '#00ff88', sub: 'Independent dual thumbsticks for shooters' },
        'fps': { svg: SVG_ICONS.crosshair, badge: 'FPS Gyro', color: '#38bdf8', sub: 'Precision shooter layout with gyro aim' },
        'racing': { svg: SVG_ICONS.racing, badge: 'Wheel & Pedals', color: '#f97316', sub: 'Tilt-to-steer wheel with touch pedals' },
        'hotas_flight': { svg: SVG_ICONS.flight, badge: 'HOTAS Flight', color: '#eab308', sub: 'Flight stick and throttle quadrant slider' },
        'mmo_action': { svg: SVG_ICONS.action, badge: 'MMO / RPG', color: '#a855f7', sub: 'Extended 8-key action bar for abilities' },
        'touchpad_only': { svg: SVG_ICONS.touchpad, badge: 'Trackpad', color: '#64748b', sub: 'Full edge-to-edge capacitive touchpad' },
        'trackpad': { svg: SVG_ICONS.touchpad, badge: 'Desktop Mouse', color: '#64748b', sub: 'Couch PC desktop mouse & scroll wheel' },
        'debug_all': { svg: SVG_ICONS.tools, badge: 'Diagnostic', color: '#06b6d4', sub: 'All virtual controls for diagnostics' },

        // --- Hardware Controller Profiles (HIDOmniPadBus) ---
        'Xbox360_WHQL': { svg: SVG_ICONS.gamepad, badge: 'ViGEm KMDF', color: '#107c10', sub: 'Universal anti-cheat compatible XInput pad' },
        'DualShock4_WHQL': { svg: SVG_ICONS.gamepad, badge: 'ViGEm KMDF', color: '#0070d1', sub: 'Sony PS4 controller emulation' },
        'DualSense_PS5': { svg: SVG_ICONS.gamepad, badge: 'HIDMaestro', color: '#0070d1', sub: 'Dual-touch capacitive touchpad + 6-axis gyro' },
        'DualSense_Edge': { svg: SVG_ICONS.gamepad, badge: 'HIDMaestro', color: '#38bdf8', sub: 'Pro controller with 2 rear paddles' },
        'Switch_Pro': { svg: SVG_ICONS.gamepad, badge: 'HIDMaestro', color: '#e60012', sub: 'Nintendo Switch Pro with gyro for emulators' },
        'JoyCon_L': { svg: SVG_ICONS.gamepad, badge: 'HIDMaestro', color: '#00bcd4', sub: 'Nintendo Joy-Con Left with SL/SR' },
        'JoyCon_R': { svg: SVG_ICONS.gamepad, badge: 'HIDMaestro', color: '#ff3d00', sub: 'Nintendo Joy-Con Right with SL/SR' },
        'GameCube': { svg: SVG_ICONS.gamepad, badge: 'HIDMaestro', color: '#a855f7', sub: 'Dolphin / Smash Bros analog pad' },
        'SteamDeck': { svg: SVG_ICONS.gamepad, badge: 'HIDMaestro', color: '#1a9fff', sub: 'Dual trackpads + L4/L5/R4/R5 grip paddles' },
        'Xbox_Elite_Series2': { svg: SVG_ICONS.gamepad, badge: 'HIDMaestro', color: '#107c10', sub: '4 rear paddles (P1-P4) + trigger stops' },
        'Xbox_Series_XS': { svg: SVG_ICONS.gamepad, badge: 'HIDMaestro', color: '#107c10', sub: 'Xbox wireless controller with Share button' },
        'RacingWheel_Logitech': { svg: SVG_ICONS.racing, badge: 'HIDMaestro', color: '#f97316', sub: 'Logitech G29 wheel tilt + pedals' },
        'FlightSim_HOTAS': { svg: SVG_ICONS.flight, badge: 'HIDMaestro', color: '#eab308', sub: 'Saitek X52 flight stick & throttle' },
        'ArcadeStick_HORI': { svg: SVG_ICONS.arcade, badge: 'HIDMaestro', color: '#ec4899', sub: 'HORI Fighting Stick Alpha 8-button' },
        'KeyboardMouse': { svg: SVG_ICONS.keyboard, badge: 'Zero-Driver', color: '#94a3b8', sub: 'Portable SendInput fallback without drivers' },

        // --- Connectivity Modes ---
        'auto': { svg: SVG_ICONS.bolt, badge: 'Smart', color: '#00ff88', sub: 'Auto-detect fastest (Wired USB > WiFi > Bluetooth)' },
        'usb': { svg: SVG_ICONS.usb, badge: 'Low Latency', color: '#38bdf8', sub: 'Direct USB loopback via ADB (7-12ms)' },
        'wifi': { svg: SVG_ICONS.wifi, badge: 'Wireless', color: '#8b5cf6', sub: 'WiFi LAN WebSocket to OmniPadServer' },
        'bluetooth': { svg: SVG_ICONS.bluetooth, badge: 'Serverless', color: '#0284c7', sub: 'Driverless Bluetooth HID Gamepad (No PC server)' },
        'offline': { svg: SVG_ICONS.offline, badge: 'Standalone', color: '#64748b', sub: 'Offline tester & customizer (Zero network)' },

        // --- Visual Themes (with SVG Theme Swatches) ---
        'theme-stealth': { svg: createThemeSwatch('#050505', '#222222', '#00ff88'), badge: 'OLED', color: '#00ff88', sub: 'OLED stealth black with emerald glow' },
        'theme-neon': { svg: createThemeSwatch('#050512', '#ff0077', '#00f0ff'), badge: 'Cyberpunk', color: '#00f0ff', sub: 'Neon magenta and cyan accents' },
        'theme-xbox': { svg: createThemeSwatch('#0a0e0a', '#107c10', '#107c10'), badge: 'Xbox', color: '#107c10', sub: 'Xbox carbon black with green glow' },
        'theme-playstation': { svg: createThemeSwatch('#080c18', '#0070d1', '#0070d1'), badge: 'PlayStation', color: '#0070d1', sub: 'PlayStation deep navy and cobalt blue' },
        'theme-retro-gb': { svg: createThemeSwatch('#c4beaa', '#8b1d3f', '#8b1d3f'), badge: 'Game Boy', color: '#8b1d3f', sub: 'Retro Game Boy DMG grey and crimson' },
        'theme-atomic-purple': { svg: createThemeSwatch('#1a0933', '#c77dff', '#c77dff'), badge: 'Atomic', color: '#c77dff', sub: 'Atomic purple 90s translucent console' },

        // --- Gyro Modes ---
        'off': { svg: SVG_ICONS.power_off, badge: 'Disabled', color: '#64748b', sub: 'Motion sensors disabled' },
        'aim': { svg: SVG_ICONS.crosshair, badge: 'Right Stick', color: '#00ff88', sub: 'FPS gyro fine aiming on Right Stick' },
        'steer': { svg: SVG_ICONS.racing, badge: 'Left Stick', color: '#f97316', sub: 'Tilt steering for racing games on Left Stick' },
        'mouse': { svg: SVG_ICONS.touchpad, badge: 'Desktop', color: '#38bdf8', sub: 'Air-mouse pointer cursor for Windows desktop' }
    };

    class OmniPadCustomSelectManager {
        constructor() {
            this.modal = null;
            this.container = null;
            this.titleEl = null;
            this.iconEl = null;
            this.activeSelect = null;
            this.enhancedSelects = new Map();
        }

        init() {
            this.modal = document.getElementById('modal-custom-picker');
            this.container = document.getElementById('custom-picker-items-container');
            this.titleEl = document.getElementById('custom-picker-title');
            this.iconEl = document.getElementById('custom-picker-icon');

            if (!this.modal || !this.container) return;

            // Close button and backdrop
            const btnClose = document.getElementById('btn-close-custom-picker');
            const backdrop = document.getElementById('backdrop-custom-picker');

            if (btnClose) {
                btnClose.addEventListener('click', (e) => {
                    if (e) e.stopPropagation();
                    this.close();
                });
            }
            if (backdrop) {
                backdrop.addEventListener('click', (e) => {
                    if (e) e.stopPropagation();
                    // Block ghost clicks synthesized from the trigger tap
                    if (performance.now() - (this.openTime || 0) < 400) return;
                    this.close();
                });
                backdrop.addEventListener('pointerdown', (e) => {
                    if (e) e.stopPropagation();
                });
            }

            // Target selects to enhance
            const targets = [
                { id: 'preset-selector', title: 'Select Controller Preset', iconSvg: SVG_ICONS.gamepad },
                { id: 'setting-controller-preset', title: 'Active Controller Profile', iconSvg: SVG_ICONS.settings },
                { id: 'setting-connectivity-mode', title: 'Connectivity & Transport Mode', iconSvg: SVG_ICONS.bolt },
                { id: 'setting-theme', title: 'Select Visual Theme', iconSvg: SVG_ICONS.palette },
                { id: 'studio-theme-select', title: 'Studio Visual Theme', iconSvg: SVG_ICONS.palette },
                { id: 'setting-gyro-mode', title: 'Gyro Motion Mode', iconSvg: SVG_ICONS.crosshair }
            ];

            targets.forEach(target => {
                const sel = document.getElementById(target.id);
                if (sel) {
                    this.enhanceSelect(sel, target.title, target.iconSvg);
                }
            });
        }

        enhanceSelect(selectEl, defaultTitle, defaultIconSvg) {
            if (this.enhancedSelects.has(selectEl)) return;

            // Hide native select visually while keeping it fully in the DOM
            selectEl.classList.add('omnipad-hidden-native-select');

            // Create custom trigger button
            const trigger = document.createElement('button');
            trigger.type = 'button';
            trigger.className = 'omnipad-select-trigger';
            trigger.id = `trigger-${selectEl.id}`;

            const iconSpan = document.createElement('span');
            iconSpan.className = 'select-trigger-icon';

            const labelSpan = document.createElement('span');
            labelSpan.className = 'select-trigger-label';

            const badgeSpan = document.createElement('span');
            badgeSpan.className = 'select-trigger-badge';

            const arrowSvg = document.createElement('span');
            arrowSvg.className = 'select-trigger-arrow select-trigger-arrow-wrap';
            arrowSvg.innerHTML = `<svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><polyline points="6 9 12 15 18 9"></polyline></svg>`;

            trigger.appendChild(iconSpan);
            trigger.appendChild(labelSpan);
            trigger.appendChild(badgeSpan);
            trigger.appendChild(arrowSvg);

            // Insert trigger next to select
            selectEl.parentNode.insertBefore(trigger, selectEl.nextSibling);

            const updateTrigger = () => {
                const val = selectEl.value;
                const opt = selectEl.querySelector(`option[value="${val}"]`) || selectEl.options[selectEl.selectedIndex];
                const text = opt ? opt.textContent.trim() : val;
                const meta = OPTION_METADATA[val] || { svg: defaultIconSvg || SVG_ICONS.gamepad, badge: '' };

                iconSpan.innerHTML = meta.svg || defaultIconSvg || SVG_ICONS.gamepad;
                labelSpan.textContent = text;
                if (meta.badge) {
                    badgeSpan.textContent = meta.badge;
                    badgeSpan.style.display = 'inline-block';
                    if (meta.color) {
                        badgeSpan.style.color = meta.color;
                        badgeSpan.style.borderColor = `${meta.color}44`;
                        badgeSpan.style.background = `${meta.color}15`;
                    }
                } else {
                    badgeSpan.style.display = 'none';
                }
            };

            updateTrigger();

            // Prevent pointerdown from bubbling up to gamepad-container (which auto-collapses topbar)
            trigger.addEventListener('pointerdown', (e) => {
                if (e) e.stopPropagation();
            });

            // Open picker modal on trigger click with debounce
            let lastOpen = 0;
            const handleOpen = (e) => {
                if (e) {
                    e.stopPropagation();
                    e.preventDefault();
                }
                const now = Date.now();
                if (now - lastOpen < 300) return;
                lastOpen = now;

                // Close top navigation drawer if open so modal has full focus
                const topBar = document.getElementById('top-bar');
                if (topBar && !topBar.classList.contains('collapsed')) {
                    topBar.classList.add('collapsed');
                    document.body.classList.remove('toolbar-open');
                }

                this.open(selectEl, defaultTitle, defaultIconSvg);
            };

            trigger.addEventListener('click', handleOpen);

            // Update trigger when select changes programmatically
            selectEl.addEventListener('change', updateTrigger);

            this.enhancedSelects.set(selectEl, { trigger, updateTrigger });
        }

        open(selectEl, title, iconSvg) {
            this.activeSelect = selectEl;
            this.openTime = performance.now();
            this.titleEl.textContent = title || selectEl.getAttribute('data-picker-title') || 'Select Option';
            this.iconEl.innerHTML = iconSvg || selectEl.getAttribute('data-picker-icon-svg') || SVG_ICONS.gamepad;
            this.container.innerHTML = '';

            const currentValue = selectEl.value;
            const groups = selectEl.querySelectorAll('optgroup');

            const renderOptionItem = (opt, container) => {
                const val = opt.value;
                const text = opt.textContent.trim();
                const isSelected = (val === currentValue);
                const meta = OPTION_METADATA[val] || { svg: SVG_ICONS.gamepad, badge: '', color: '#38bdf8', sub: '' };

                const item = document.createElement('div');
                item.className = `picker-item ${isSelected ? 'selected' : ''}`;

                const leftWrap = document.createElement('div');
                leftWrap.className = 'picker-item-left';

                const itemIcon = document.createElement('span');
                itemIcon.className = 'picker-item-icon';
                itemIcon.innerHTML = meta.svg;
                if (meta.color) {
                    itemIcon.style.boxShadow = `0 0 10px ${meta.color}28`;
                    itemIcon.style.borderColor = `${meta.color}55`;
                }

                const textWrap = document.createElement('div');
                textWrap.className = 'picker-item-text';

                const nameRow = document.createElement('div');
                nameRow.className = 'picker-item-title-row';

                const nameSpan = document.createElement('span');
                nameSpan.className = 'picker-item-name';
                nameSpan.textContent = text;
                nameRow.appendChild(nameSpan);

                if (meta.badge) {
                    const badge = document.createElement('span');
                    badge.className = 'picker-item-badge';
                    badge.textContent = meta.badge;
                    if (meta.color) {
                        badge.style.color = meta.color;
                        badge.style.borderColor = `${meta.color}55`;
                        badge.style.background = `${meta.color}18`;
                    }
                    nameRow.appendChild(badge);
                }

                textWrap.appendChild(nameRow);

                if (meta.sub) {
                    const subSpan = document.createElement('span');
                    subSpan.className = 'picker-item-sub';
                    subSpan.textContent = meta.sub;
                    textWrap.appendChild(subSpan);
                }

                leftWrap.appendChild(itemIcon);
                leftWrap.appendChild(textWrap);
                item.appendChild(leftWrap);

                const checkWrap = document.createElement('div');
                checkWrap.className = 'picker-item-check';
                if (isSelected) {
                    checkWrap.innerHTML = SVG_ICONS.check;
                }
                item.appendChild(checkWrap);

                // Tap handler for option items (click with debounce, safe for scrolling)
                let lastSelect = 0;
                const handleSelect = (e) => {
                    if (e) {
                        e.stopPropagation();
                        e.preventDefault();
                    }
                    const now = Date.now();
                    if (now - lastSelect < 300) return;
                    lastSelect = now;

                    try { navigator.vibrate(12); } catch (_) { }

                    selectEl.value = val;
                    selectEl.dispatchEvent(new Event('change', { bubbles: true }));

                    const info = this.enhancedSelects.get(selectEl);
                    if (info) info.updateTrigger();

                    // If user selected a theme from setting-theme or studio-theme-select, apply theme immediately
                    if (selectEl.id === 'setting-theme' || selectEl.id === 'studio-theme-select') {
                        document.body.className = document.body.className.replace(/theme-[a-z0-9-]+/g, '').trim();
                        document.body.classList.add(val);
                        localStorage.setItem('omnipad_theme', val);
                    }

                    this.close();
                };

                item.addEventListener('pointerdown', (e) => {
                    if (e) e.stopPropagation();
                });
                item.addEventListener('click', handleSelect);

                container.appendChild(item);
            };

            if (groups.length > 0) {
                groups.forEach(grp => {
                    const grpHeader = document.createElement('div');
                    grpHeader.className = 'picker-group-header';

                    const grpTitle = document.createElement('span');
                    grpTitle.className = 'picker-group-title';
                    grpTitle.textContent = grp.label;

                    grpHeader.appendChild(grpTitle);
                    this.container.appendChild(grpHeader);

                    const opts = grp.querySelectorAll('option');
                    opts.forEach(opt => renderOptionItem(opt, this.container));
                });
            } else {
                const opts = selectEl.querySelectorAll('option');
                opts.forEach(opt => renderOptionItem(opt, this.container));
            }

            this.modal.classList.remove('hidden');
            document.body.classList.add('modal-open');

            // Scroll selected item into view smoothly
            setTimeout(() => {
                const selItem = this.container.querySelector('.picker-item.selected');
                if (selItem) {
                    selItem.scrollIntoView({ behavior: 'smooth', block: 'center' });
                }
            }, 60);
        }

        close() {
            if (this.modal) {
                this.modal.classList.add('hidden');
            }
            // Check if any other modal is open before removing modal-open
            const openModals = document.querySelectorAll('.modal:not(.hidden)');
            if (openModals.length === 0) {
                document.body.classList.remove('modal-open');
            }
            this.activeSelect = null;
        }

        updateAllTriggers() {
            this.enhancedSelects.forEach(info => {
                if (info && typeof info.updateTrigger === 'function') {
                    info.updateTrigger();
                }
            });
        }
    }

    // Export global instance & icons
    window.OMNIPAD_SVG_ICONS = SVG_ICONS;
    window.omniPadSelect = new OmniPadCustomSelectManager();
    window.omniPadSelect.icons = SVG_ICONS;

    // Auto-boot when DOM is ready
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', () => window.omniPadSelect.init());
    } else {
        window.omniPadSelect.init();
    }
})();
