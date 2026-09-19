/**
 * OmniPad Layout Presets
 * Stored in normalized coordinates (x: 0.0 to 1.0, y: 0.0 to 1.0) for aspect-ratio independence.
 * Designed & tested for mobile landscape viewports (e.g. 844x390).
 */
const LAYOUT_PRESETS = {
    // 1. Xbox 360 Standard
    xbox: [
        // Top Shoulders & Triggers
        { id: 'lt', type: 'trigger', binding: 'LT', label: 'LT', x: 0.08, y: 0.10, w: 82, h: 44, shape: 'shoulder' },
        { id: 'lb', type: 'button', binding: 0x0100, label: 'LB', x: 0.20, y: 0.10, w: 88, h: 44, shape: 'shoulder' },
        { id: 'rb', type: 'button', binding: 0x0200, label: 'RB', x: 0.80, y: 0.10, w: 88, h: 44, shape: 'shoulder' },
        { id: 'rt', type: 'trigger', binding: 'RT', label: 'RT', x: 0.92, y: 0.10, w: 82, h: 44, shape: 'shoulder' },

        // Center System Buttons (Safely below status pill)
        { id: 'back', type: 'button', binding: 0x0020, label: 'VIEW', svg: 'view', x: 0.42, y: 0.18, w: 46, h: 46 },
        { id: 'guide', type: 'button', binding: 0x0400, label: 'XBOX', svg: 'guide', x: 0.50, y: 0.18, w: 50, h: 50 },
        { id: 'start', type: 'button', binding: 0x0010, label: 'MENU', svg: 'menu', x: 0.58, y: 0.18, w: 46, h: 46 },

        // Left Controls (Asymmetric Stick Top-Left, D-Pad Bottom-Left)
        { id: 'ls', type: 'joystick', binding: 'left', x: 0.16, y: 0.38, w: 148, h: 148 },
        { id: 'dpad', type: 'dpad', x: 0.24, y: 0.78, w: 148, h: 148 },

        // Right Controls (Diamond Face Buttons Top-Right, Right Stick Bottom-Right)
        { id: 'btn_y', type: 'button', binding: 0x8000, label: 'Y', btnClass: 'btn-y', x: 0.85, y: 0.24, w: 66, h: 66 },
        { id: 'btn_x', type: 'button', binding: 0x4000, label: 'X', btnClass: 'btn-x', x: 0.75, y: 0.38, w: 66, h: 66 },
        { id: 'btn_b', type: 'button', binding: 0x2000, label: 'B', btnClass: 'btn-b', x: 0.95, y: 0.38, w: 66, h: 66 },
        { id: 'btn_a', type: 'button', binding: 0x1000, label: 'A', btnClass: 'btn-a', x: 0.85, y: 0.52, w: 66, h: 66 },
        { id: 'rs', type: 'joystick', binding: 'right', x: 0.72, y: 0.78, w: 148, h: 148 }
    ],

    // 2. PlayStation DualShock (Symmetrical Bottom Sticks + Center Touchpad)
    playstation: [
        // Top Shoulders & Triggers
        { id: 'l2', type: 'trigger', binding: 'LT', label: 'L2', x: 0.08, y: 0.10, w: 82, h: 44, shape: 'shoulder' },
        { id: 'l1', type: 'button', binding: 0x0100, label: 'L1', x: 0.20, y: 0.10, w: 88, h: 44, shape: 'shoulder' },
        { id: 'r1', type: 'button', binding: 0x0200, label: 'R1', x: 0.80, y: 0.10, w: 88, h: 44, shape: 'shoulder' },
        { id: 'r2', type: 'trigger', binding: 'RT', label: 'R2', x: 0.92, y: 0.10, w: 82, h: 44, shape: 'shoulder' },

        // Center System Buttons
        { id: 'share', type: 'button', binding: 0x0020, label: 'SHARE', x: 0.38, y: 0.14, w: 66, h: 32, shape: 'pill' },
        { id: 'options', type: 'button', binding: 0x0010, label: 'OPTIONS', x: 0.62, y: 0.14, w: 74, h: 32, shape: 'pill' },

        // Center DualShock 4 Touchpad
        { id: 'ps_touchpad', type: 'touchpad', label: 'TOUCHPAD', x: 0.50, y: 0.34, w: 200, h: 92 },

        // Left Side: D-Pad Top-Left
        { id: 'dpad', type: 'dpad', x: 0.16, y: 0.41, w: 155, h: 155 },

        // Right Side: Shapes Top-Right
        { id: 'btn_tri', type: 'button', binding: 0x8000, label: '▲', btnClass: 'btn-ps-tri', x: 0.85, y: 0.26, w: 66, h: 66 },
        { id: 'btn_sqr', type: 'button', binding: 0x4000, label: '■', btnClass: 'btn-ps-sqr', x: 0.75, y: 0.41, w: 66, h: 66 },
        { id: 'btn_cir', type: 'button', binding: 0x2000, label: '●', btnClass: 'btn-ps-cir', x: 0.95, y: 0.41, w: 66, h: 66 },
        { id: 'btn_crs', type: 'button', binding: 0x1000, label: '✖', btnClass: 'btn-ps-crs', x: 0.85, y: 0.56, w: 66, h: 66 },

        // Symmetrical Sticks on Bottom
        { id: 'ls', type: 'joystick', binding: 'left', x: 0.33, y: 0.75, w: 145, h: 145 },
        { id: 'rs', type: 'joystick', binding: 'right', x: 0.67, y: 0.75, w: 145, h: 145 }
    ],

    // 3. Nintendo Switch Pro (Authentic Inverted A/B, X/Y + Home & Capture)
    switch_pro: [
        { id: 'zl', type: 'trigger', binding: 'LT', label: 'ZL', x: 0.08, y: 0.10, w: 82, h: 44, shape: 'shoulder' },
        { id: 'l', type: 'button', binding: 0x0100, label: 'L', x: 0.20, y: 0.10, w: 88, h: 44, shape: 'shoulder' },
        { id: 'r', type: 'button', binding: 0x0200, label: 'R', x: 0.80, y: 0.10, w: 88, h: 44, shape: 'shoulder' },
        { id: 'zr', type: 'trigger', binding: 'RT', label: 'ZR', x: 0.92, y: 0.10, w: 82, h: 44, shape: 'shoulder' },

        { id: 'btn_minus', type: 'button', binding: 0x0020, label: '−', x: 0.38, y: 0.18, w: 46, h: 46, shape: 'action' },
        { id: 'btn_capture', type: 'button', binding: 0x0800, label: '⚲', x: 0.46, y: 0.22, w: 40, h: 40, shape: 'action' },
        { id: 'btn_home', type: 'button', binding: 0x0400, label: '⌂', x: 0.54, y: 0.22, w: 40, h: 40, shape: 'action' },
        { id: 'btn_plus', type: 'button', binding: 0x0010, label: '+', x: 0.62, y: 0.18, w: 46, h: 46, shape: 'action' },

        { id: 'ls', type: 'joystick', binding: 'left', x: 0.16, y: 0.38, w: 148, h: 148 },
        { id: 'dpad', type: 'dpad', x: 0.24, y: 0.78, w: 148, h: 148 },

        // Nintendo Diamond: Top X, Left Y, Right A, Bottom B
        { id: 'btn_x', type: 'button', binding: 0x8000, label: 'X', btnClass: 'btn-x', x: 0.85, y: 0.24, w: 66, h: 66 },
        { id: 'btn_y', type: 'button', binding: 0x4000, label: 'Y', btnClass: 'btn-y', x: 0.75, y: 0.38, w: 66, h: 66 },
        { id: 'btn_a', type: 'button', binding: 0x2000, label: 'A', btnClass: 'btn-b', x: 0.95, y: 0.38, w: 66, h: 66 },
        { id: 'btn_b', type: 'button', binding: 0x1000, label: 'B', btnClass: 'btn-a', x: 0.85, y: 0.52, w: 66, h: 66 },
        { id: 'rs', type: 'joystick', binding: 'right', x: 0.72, y: 0.78, w: 148, h: 148 }
    ],

    // 3. Authentic Arcade Fightstick (Sanwa 8-Button Vewlix Curve + Ball-Top Stick)
    arcade: [
        // Top Service / System Controls
        { id: 'btn_l3', type: 'button', binding: 0x0040, label: 'L3', x: 0.10, y: 0.15, w: 50, h: 36, shape: 'pill' },
        { id: 'btn_coin', type: 'button', binding: 0x0020, label: 'COIN', btnClass: 'arcade-coin', x: 0.38, y: 0.15, w: 74, h: 36, shape: 'pill' },
        { id: 'btn_home', type: 'button', binding: 0x0400, label: 'HOME', btnClass: 'arcade-home', x: 0.50, y: 0.15, w: 46, h: 46, shape: 'action' },
        { id: 'btn_start', type: 'button', binding: 0x0010, label: '1P START', btnClass: 'arcade-start', x: 0.62, y: 0.15, w: 90, h: 36, shape: 'pill' },
        { id: 'btn_r3', type: 'button', binding: 0x0080, label: 'R3', x: 0.90, y: 0.15, w: 50, h: 36, shape: 'pill' },

        // Left Side: Sanwa Red Ball-Top Joystick (Auto-mirrored to Left Stick & D-pad)
        { id: 'arcade_stick', type: 'joystick', binding: 'arcade', x: 0.22, y: 0.58, w: 185, h: 185, btnClass: 'arcade-joystick' },

        // Right Side: Ergonomic 8-Button Vewlix Curved Arcade Layout
        // Row 1 (Punches): LP (X), MP (Y), HP (RB), 3P (LB)
        { id: 'btn_lp', type: 'button', binding: 0x4000, label: 'LP\nX', btnClass: 'arcade-btn arcade-btn-lp', x: 0.52, y: 0.44, w: 68, h: 68, shape: 'action' },
        { id: 'btn_mp', type: 'button', binding: 0x8000, label: 'MP\nY', btnClass: 'arcade-btn arcade-btn-mp', x: 0.65, y: 0.36, w: 68, h: 68, shape: 'action' },
        { id: 'btn_hp', type: 'button', binding: 0x0200, label: 'HP\nRB', btnClass: 'arcade-btn arcade-btn-hp', x: 0.78, y: 0.40, w: 68, h: 68, shape: 'action' },
        { id: 'btn_3p', type: 'button', binding: 0x0100, label: '3P\nLB', btnClass: 'arcade-btn arcade-btn-3p', x: 0.91, y: 0.48, w: 68, h: 68, shape: 'action' },

        // Row 2 (Kicks): LK (A), MK (B), HK (RT), 3K (LT)
        { id: 'btn_lk', type: 'button', binding: 0x1000, label: 'LK\nA', btnClass: 'arcade-btn arcade-btn-lk', x: 0.52, y: 0.70, w: 68, h: 68, shape: 'action' },
        { id: 'btn_mk', type: 'button', binding: 0x2000, label: 'MK\nB', btnClass: 'arcade-btn arcade-btn-mk', x: 0.65, y: 0.62, w: 68, h: 68, shape: 'action' },
        { id: 'btn_hk', type: 'trigger', binding: 'RT', label: 'HK\nRT', btnClass: 'arcade-btn arcade-btn-hk', x: 0.78, y: 0.66, w: 68, h: 68, shape: 'action' },
        { id: 'btn_3k', type: 'trigger', binding: 'LT', label: '3K\nLT', btnClass: 'arcade-btn arcade-btn-3k', x: 0.91, y: 0.74, w: 68, h: 68, shape: 'action' }
    ],

    // 4. Tekken Hitbox (8-Way Directional Grid + Curved Arcade Buttons)
    hitbox: [
        // Top Shoulders & System
        { id: 'lt', type: 'button', binding: 0x0040, label: 'LT (3+4)', x: 0.08, y: 0.10, w: 90, h: 44, shape: 'shoulder' },
        { id: 'lb', type: 'button', binding: 0x0100, label: 'LB (1+2)', x: 0.21, y: 0.10, w: 90, h: 44, shape: 'shoulder' },
        { id: 'select', type: 'button', binding: 0x0020, label: 'SELECT', x: 0.42, y: 0.15, w: 76, h: 36, shape: 'pill' },
        { id: 'start', type: 'button', binding: 0x0010, label: 'START', x: 0.58, y: 0.15, w: 76, h: 36, shape: 'pill' },

        // Left Side: 8-Way Directional Grid
        { id: 'dpad_8way', type: 'dpad_matrix', x: 0.19, y: 0.58, w: 200, h: 200 },

        // Right Side: Arcade 8-Button Layout (Ergonomic curve, 66px diameter)
        { id: 'btn_1', type: 'button', binding: 0x4000, label: '1 (X)', btnClass: 'btn-x', x: 0.62, y: 0.40, w: 66, h: 66, shape: 'action' },
        { id: 'btn_3', type: 'button', binding: 0x1000, label: '3 (A)', btnClass: 'btn-a', x: 0.62, y: 0.68, w: 66, h: 66, shape: 'action' },

        { id: 'btn_2', type: 'button', binding: 0x8000, label: '2 (Y)', btnClass: 'btn-y', x: 0.74, y: 0.34, w: 66, h: 66, shape: 'action' },
        { id: 'btn_4', type: 'button', binding: 0x2000, label: '4 (B)', btnClass: 'btn-b', x: 0.74, y: 0.62, w: 66, h: 66, shape: 'action' },

        { id: 'btn_12', type: 'combo', combo: ['X', 'Y'], label: '1+2', x: 0.86, y: 0.34, w: 64, h: 64, shape: 'action' },
        { id: 'btn_34', type: 'combo', combo: ['A', 'B'], label: '3+4', x: 0.86, y: 0.62, w: 64, h: 64, shape: 'action' },
        { id: 'btn_rage', type: 'macro', macroId: 'macro_ewgf', label: 'EWGF', x: 0.86, y: 0.83, w: 76, h: 44, shape: 'pill' }
    ],

    // 5. Racing Wheel & Pedals
    racing: [
        { id: 'paddle_down', type: 'button', binding: 0x0100, label: '◄ GEAR -', x: 0.14, y: 0.10, w: 110, h: 46, shape: 'shoulder' },
        { id: 'pedal_brake', type: 'pedal', binding: 'LT', label: 'BRAKE', x: 0.14, y: 0.58, w: 100, h: 210 },

        { id: 'pause', type: 'button', binding: 0x0010, label: 'PAUSE', x: 0.50, y: 0.16, w: 86, h: 38, shape: 'pill' },
        { id: 'wheel_center', type: 'wheel_indicator', x: 0.50, y: 0.44, w: 175, h: 175 },
        { id: 'btn_handbrake', type: 'button', binding: 0x1000, label: 'HANDBRAKE', x: 0.50, y: 0.82, w: 160, h: 44, shape: 'pill' },

        { id: 'paddle_up', type: 'button', binding: 0x0200, label: 'GEAR + ►', x: 0.86, y: 0.10, w: 110, h: 46, shape: 'shoulder' },
        { id: 'pedal_gas', type: 'pedal', binding: 'RT', label: 'GAS', x: 0.86, y: 0.58, w: 100, h: 210 }
    ],

    // 6. FPS Precision (Look Pad + Gyro Aim + Hair Triggers)
    fps: [
        { id: 'btn_ads', type: 'trigger', binding: 'LT', label: 'ADS (Aim)', x: 0.15, y: 0.10, w: 110, h: 46, shape: 'shoulder' },
        { id: 'btn_pause', type: 'button', binding: 0x0010, label: 'PAUSE', x: 0.50, y: 0.12, w: 80, h: 36, shape: 'pill' },
        { id: 'move_stick', type: 'joystick', binding: 'left', x: 0.18, y: 0.60, w: 155, h: 155 },

        { id: 'btn_reload', type: 'button', binding: 0x4000, label: 'Reload (X)', btnClass: 'btn-x', x: 0.45, y: 0.24, w: 68, h: 68, shape: 'action' },

        { id: 'aim_surface', type: 'trackpad_aim', x: 0.66, y: 0.56, w: 260, h: 200 },
        { id: 'btn_fire', type: 'trigger', binding: 'RT', label: 'FIRE', x: 0.85, y: 0.10, w: 110, h: 46, shape: 'shoulder' },
        { id: 'btn_jump', type: 'button', binding: 0x1000, label: 'Jump (A)', btnClass: 'btn-a', x: 0.90, y: 0.45, w: 66, h: 66, shape: 'action' },
        { id: 'btn_crouch', type: 'button', binding: 0x2000, label: 'Slide (B)', btnClass: 'btn-b', x: 0.90, y: 0.72, w: 66, h: 66, shape: 'action' }
    ],

    // 7. Retro SNES / Classic (Authentic D-Pad + 4 Face + L/R Bumpers)
    snes_retro: [
        { id: 'l_bumper', type: 'button', binding: 0x0100, label: 'L', x: 0.16, y: 0.12, w: 100, h: 44, shape: 'shoulder' },
        { id: 'r_bumper', type: 'button', binding: 0x0200, label: 'R', x: 0.84, y: 0.12, w: 100, h: 44, shape: 'shoulder' },

        { id: 'dpad', type: 'dpad', x: 0.22, y: 0.52, w: 165, h: 165 },

        { id: 'select', type: 'button', binding: 0x0020, label: 'SELECT', x: 0.44, y: 0.76, w: 76, h: 36, shape: 'pill' },
        { id: 'start', type: 'button', binding: 0x0010, label: 'START', x: 0.56, y: 0.76, w: 76, h: 36, shape: 'pill' },

        // SNES Diamond: Top X, Left Y, Right A, Bottom B
        { id: 'btn_x', type: 'button', binding: 0x8000, label: 'X', btnClass: 'btn-x', x: 0.82, y: 0.32, w: 68, h: 68 },
        { id: 'btn_y', type: 'button', binding: 0x4000, label: 'Y', btnClass: 'btn-y', x: 0.72, y: 0.46, w: 68, h: 68 },
        { id: 'btn_a', type: 'button', binding: 0x2000, label: 'A', btnClass: 'btn-b', x: 0.92, y: 0.46, w: 68, h: 68 },
        { id: 'btn_b', type: 'button', binding: 0x1000, label: 'B', btnClass: 'btn-a', x: 0.82, y: 0.60, w: 68, h: 68 }
    ],

    // 8. Touchpad-Only (Edge-to-Edge PS4/PS5 Trackpad for External Controller & Phone Clip Users)
    touchpad_only: [
        { id: 'l1', type: 'button', binding: 0x0100, label: 'L1', x: 0.10, y: 0.12, w: 90, h: 44, shape: 'shoulder' },
        { id: 'share', type: 'button', binding: 0x0020, label: 'SHARE', x: 0.38, y: 0.12, w: 70, h: 36, shape: 'pill' },
        { id: 'options', type: 'button', binding: 0x0010, label: 'OPTIONS', x: 0.62, y: 0.12, w: 80, h: 36, shape: 'pill' },
        { id: 'r1', type: 'button', binding: 0x0200, label: 'R1', x: 0.90, y: 0.12, w: 90, h: 44, shape: 'shoulder' },
        { id: 'l3', type: 'button', binding: 0x0040, label: 'L3', x: 0.10, y: 0.86, w: 70, h: 44, shape: 'pill' },
        { id: 'r3', type: 'button', binding: 0x0080, label: 'R3', x: 0.90, y: 0.86, w: 70, h: 44, shape: 'pill' },

        { id: 'full_touchpad', type: 'touchpad', label: 'DUALSHOCK 4 TOUCHPAD', x: 0.50, y: 0.54, w: 560, h: 220 }
    ],

    // 10. Couch PC Trackpad & Media Remote
    trackpad: [
        { id: 'key_esc', type: 'button', binding: 0x0020, label: 'Esc', x: 0.10, y: 0.10, w: 56, h: 38, shape: 'key' },
        { id: 'key_tab', type: 'button', binding: 0x0100, label: 'Tab', x: 0.20, y: 0.10, w: 56, h: 38, shape: 'key' },
        { id: 'key_win', type: 'button', binding: 0x0400, label: 'Win', x: 0.30, y: 0.10, w: 56, h: 38, shape: 'key' },
        { id: 'key_space', type: 'button', binding: 0x1000, label: 'Space', x: 0.72, y: 0.10, w: 76, h: 38, shape: 'key' },
        { id: 'key_enter', type: 'button', binding: 0x0010, label: 'Enter', x: 0.85, y: 0.10, w: 68, h: 38, shape: 'key' },

        { id: 'trackpad_main', type: 'touchpad', label: 'DESKTOP TRACKPAD', x: 0.50, y: 0.48, w: 460, h: 200 },
        { id: 'click_left', type: 'trigger', binding: 'LT', label: 'Left Click', x: 0.36, y: 0.84, w: 140, h: 50, shape: 'shoulder' },
        { id: 'click_right', type: 'trigger', binding: 'RT', label: 'Right Click', x: 0.64, y: 0.84, w: 140, h: 50, shape: 'shoulder' }
    ],

    // 11. Nintendo GameCube (Authentic Asymmetric Layout + C-Stick)
    gamecube: [
        // Top Shoulders & Triggers
        { id: 'l', type: 'trigger', binding: 'LT', label: 'L', x: 0.12, y: 0.10, w: 88, h: 44, shape: 'shoulder' },
        { id: 'z', type: 'button', binding: 0x0200, label: 'Z', btnClass: 'btn-gc-z', x: 0.74, y: 0.10, w: 76, h: 44, shape: 'shoulder' },
        { id: 'r', type: 'trigger', binding: 'RT', label: 'R', x: 0.88, y: 0.10, w: 88, h: 44, shape: 'shoulder' },

        // Center Start/Pause
        { id: 'start', type: 'button', binding: 0x0010, label: 'START\nPAUSE', btnClass: 'btn-gc-start', x: 0.50, y: 0.22, w: 56, h: 56, shape: 'action' },

        // Left Controls: Octagonal Analog Stick + Lower D-Pad
        { id: 'ls', type: 'joystick', binding: 'left', x: 0.16, y: 0.40, w: 155, h: 155 },
        { id: 'dpad', type: 'dpad', x: 0.28, y: 0.76, w: 125, h: 125 },

        // Right Controls: Giant Green A, Red B, Kidney X/Y, Yellow C-Stick
        { id: 'btn_a', type: 'button', binding: 0x1000, label: 'A', btnClass: 'btn-gc-a', x: 0.80, y: 0.46, w: 86, h: 86, shape: 'action' },
        { id: 'btn_b', type: 'button', binding: 0x2000, label: 'B', btnClass: 'btn-gc-b', x: 0.68, y: 0.52, w: 54, h: 54, shape: 'action' },
        { id: 'btn_y', type: 'button', binding: 0x8000, label: 'Y', btnClass: 'btn-gc-y', x: 0.77, y: 0.28, w: 64, h: 46, shape: 'pill' },
        { id: 'btn_x', type: 'button', binding: 0x4000, label: 'X', btnClass: 'btn-gc-x', x: 0.92, y: 0.40, w: 46, h: 64, shape: 'pill' },
        { id: 'rs', type: 'joystick', binding: 'right', btnClass: 'btn-gc-cstick', x: 0.67, y: 0.78, w: 125, h: 125 }
    ],

    // 12. Nintendo 64 (Analog Stick + 4 C-Buttons + Z-Trigger)
    n64: [
        // Top Shoulders & Z-Trigger
        { id: 'l', type: 'button', binding: 0x0100, label: 'L', x: 0.14, y: 0.10, w: 88, h: 44, shape: 'shoulder' },
        { id: 'z_trig', type: 'trigger', binding: 'LT', label: 'Z-TRIG', btnClass: 'btn-n64-z', x: 0.30, y: 0.10, w: 92, h: 44, shape: 'shoulder' },
        { id: 'r', type: 'button', binding: 0x0200, label: 'R', x: 0.86, y: 0.10, w: 88, h: 44, shape: 'shoulder' },

        // Center Start Button
        { id: 'start', type: 'button', binding: 0x0010, label: 'START', btnClass: 'btn-n64-start', x: 0.50, y: 0.20, w: 54, h: 54, shape: 'action' },

        // Left / Middle Prong Controls
        { id: 'dpad', type: 'dpad', x: 0.14, y: 0.52, w: 135, h: 135 },
        { id: 'ls', type: 'joystick', binding: 'left', x: 0.35, y: 0.58, w: 155, h: 155 },

        // Right Controls: Blue A, Green B, 4 Yellow C-Buttons
        { id: 'btn_b', type: 'button', binding: 0x2000, label: 'B', btnClass: 'btn-n64-b', x: 0.57, y: 0.54, w: 64, h: 64, shape: 'action' },
        { id: 'btn_a', type: 'button', binding: 0x1000, label: 'A', btnClass: 'btn-n64-a', x: 0.64, y: 0.68, w: 68, h: 68, shape: 'action' },

        { id: 'c_up', type: 'button', binding: 0x8000, label: '▲ C', btnClass: 'btn-n64-c', x: 0.84, y: 0.32, w: 52, h: 52, shape: 'action' },
        { id: 'c_left', type: 'button', binding: 0x4000, label: '◀ C', btnClass: 'btn-n64-c', x: 0.74, y: 0.44, w: 52, h: 52, shape: 'action' },
        { id: 'c_right', type: 'button', binding: 0x0080, label: 'C ▶', btnClass: 'btn-n64-c', x: 0.94, y: 0.44, w: 52, h: 52, shape: 'action' },
        { id: 'c_down', type: 'button', binding: 0x0040, label: '▼ C', btnClass: 'btn-n64-c', x: 0.84, y: 0.56, w: 52, h: 52, shape: 'action' }
    ],

    // 13. Sega Saturn / Genesis 6-Button Fightpad
    sega_saturn: [
        // Shoulders
        { id: 'l', type: 'trigger', binding: 'LT', label: 'L', x: 0.12, y: 0.10, w: 88, h: 44, shape: 'shoulder' },
        { id: 'r', type: 'trigger', binding: 'RT', label: 'R', x: 0.88, y: 0.10, w: 88, h: 44, shape: 'shoulder' },

        // Center Mode & Start
        { id: 'mode', type: 'button', binding: 0x0020, label: 'MODE', x: 0.42, y: 0.18, w: 72, h: 34, shape: 'pill' },
        { id: 'start', type: 'button', binding: 0x0010, label: 'START', btnClass: 'btn-sega-start', x: 0.58, y: 0.18, w: 76, h: 34, shape: 'pill' },

        // Left 8-Way D-Pad
        { id: 'dpad', type: 'dpad', x: 0.20, y: 0.55, w: 165, h: 165 },

        // Right 6 Face Buttons (Top: X, Y, Z; Bottom: A, B, C)
        { id: 'btn_x', type: 'button', binding: 0x4000, label: 'X', btnClass: 'btn-sega-top', x: 0.62, y: 0.36, w: 60, h: 60, shape: 'action' },
        { id: 'btn_y', type: 'button', binding: 0x8000, label: 'Y', btnClass: 'btn-sega-top', x: 0.74, y: 0.32, w: 60, h: 60, shape: 'action' },
        { id: 'btn_z', type: 'button', binding: 0x0100, label: 'Z', btnClass: 'btn-sega-top', x: 0.86, y: 0.36, w: 60, h: 60, shape: 'action' },

        { id: 'btn_a', type: 'button', binding: 0x1000, label: 'A', btnClass: 'btn-sega-bot', x: 0.62, y: 0.60, w: 64, h: 64, shape: 'action' },
        { id: 'btn_b', type: 'button', binding: 0x2000, label: 'B', btnClass: 'btn-sega-bot', x: 0.74, y: 0.56, w: 64, h: 64, shape: 'action' },
        { id: 'btn_c', type: 'button', binding: 0x0200, label: 'C', btnClass: 'btn-sega-bot', x: 0.86, y: 0.60, w: 64, h: 64, shape: 'action' }
    ],

    // 14. PlayStation 1 Classic (Pre-DualShock PSX)
    ps1_classic: [
        // Top Shoulders (L1, L2, R1, R2)
        { id: 'l2', type: 'button', binding: 0x0040, label: 'L2', x: 0.08, y: 0.10, w: 82, h: 44, shape: 'shoulder' },
        { id: 'l1', type: 'button', binding: 0x0100, label: 'L1', x: 0.20, y: 0.10, w: 88, h: 44, shape: 'shoulder' },
        { id: 'r1', type: 'button', binding: 0x0200, label: 'R1', x: 0.80, y: 0.10, w: 88, h: 44, shape: 'shoulder' },
        { id: 'r2', type: 'button', binding: 0x0080, label: 'R2', x: 0.92, y: 0.10, w: 82, h: 44, shape: 'shoulder' },

        // Center Select & Start
        { id: 'select', type: 'button', binding: 0x0020, label: 'SELECT', x: 0.42, y: 0.76, w: 76, h: 36, shape: 'pill' },
        { id: 'start', type: 'button', binding: 0x0010, label: 'START', x: 0.58, y: 0.76, w: 76, h: 36, shape: 'pill' },

        // Left Side: Classic PSX D-Pad
        { id: 'dpad', type: 'dpad', x: 0.22, y: 0.50, w: 175, h: 175 },

        // Right Side: 4 Geometric Shapes
        { id: 'btn_tri', type: 'button', binding: 0x8000, label: '▲', btnClass: 'btn-ps-tri', x: 0.82, y: 0.30, w: 72, h: 72 },
        { id: 'btn_sqr', type: 'button', binding: 0x4000, label: '■', btnClass: 'btn-ps-sqr', x: 0.71, y: 0.47, w: 72, h: 72 },
        { id: 'btn_cir', type: 'button', binding: 0x2000, label: '●', btnClass: 'btn-ps-cir', x: 0.93, y: 0.47, w: 72, h: 72 },
        { id: 'btn_crs', type: 'button', binding: 0x1000, label: '✖', btnClass: 'btn-ps-crs', x: 0.82, y: 0.64, w: 72, h: 72 }
    ],

    // 15. NES / Game Boy Classic (8-Bit Retro)
    nes_retro: [
        // Left D-Pad
        { id: 'dpad', type: 'dpad', x: 0.22, y: 0.52, w: 165, h: 165 },

        // Center Select & Start
        { id: 'select', type: 'button', binding: 0x0020, label: 'SELECT', x: 0.44, y: 0.72, w: 76, h: 34, shape: 'pill' },
        { id: 'start', type: 'button', binding: 0x0010, label: 'START', x: 0.56, y: 0.72, w: 76, h: 34, shape: 'pill' },

        // Turbo Buttons on Top
        { id: 'btn_tb', type: 'button', binding: 0x4000, label: 'TURBO B', btnClass: 'btn-nes-turbo', x: 0.74, y: 0.24, w: 74, h: 42, shape: 'pill' },
        { id: 'btn_ta', type: 'button', binding: 0x8000, label: 'TURBO A', btnClass: 'btn-nes-turbo', x: 0.88, y: 0.18, w: 74, h: 42, shape: 'pill' },

        // Angled B and A Buttons
        { id: 'btn_b', type: 'button', binding: 0x2000, label: 'B', btnClass: 'btn-nes-b', x: 0.74, y: 0.52, w: 72, h: 72, shape: 'action' },
        { id: 'btn_a', type: 'button', binding: 0x1000, label: 'A', btnClass: 'btn-nes-a', x: 0.88, y: 0.44, w: 72, h: 72, shape: 'action' }
    ],

    // 16. Nintendo 3DS / Dual Screen Handheld
    ds_3ds: [
        // Top Shoulders
        { id: 'zl', type: 'trigger', binding: 'LT', label: 'ZL', x: 0.08, y: 0.10, w: 80, h: 44, shape: 'shoulder' },
        { id: 'l', type: 'button', binding: 0x0100, label: 'L', x: 0.20, y: 0.10, w: 84, h: 44, shape: 'shoulder' },
        { id: 'r', type: 'button', binding: 0x0200, label: 'R', x: 0.80, y: 0.10, w: 84, h: 44, shape: 'shoulder' },
        { id: 'zr', type: 'trigger', binding: 'RT', label: 'ZR', x: 0.92, y: 0.10, w: 80, h: 44, shape: 'shoulder' },

        // Center Stylus Touchscreen Region
        { id: 'ds_touch', type: 'touchpad', label: '3DS TOUCH SCREEN (STYLUS)', x: 0.50, y: 0.48, w: 230, h: 140 },

        // Bottom Start & Select
        { id: 'select', type: 'button', binding: 0x0020, label: 'SELECT', x: 0.42, y: 0.86, w: 74, h: 32, shape: 'pill' },
        { id: 'start', type: 'button', binding: 0x0010, label: 'START', x: 0.58, y: 0.86, w: 74, h: 32, shape: 'pill' },

        // Left Controls: Circle Pad (Top) + D-Pad (Bottom)
        { id: 'circle_pad', type: 'joystick', binding: 'left', x: 0.16, y: 0.38, w: 145, h: 145 },
        { id: 'dpad', type: 'dpad', x: 0.22, y: 0.78, w: 130, h: 130 },

        // Right Controls: Nintendo ABXY Diamond
        { id: 'btn_x', type: 'button', binding: 0x8000, label: 'X', btnClass: 'btn-x', x: 0.85, y: 0.28, w: 62, h: 62 },
        { id: 'btn_y', type: 'button', binding: 0x4000, label: 'Y', btnClass: 'btn-y', x: 0.75, y: 0.42, w: 62, h: 62 },
        { id: 'btn_a', type: 'button', binding: 0x2000, label: 'A', btnClass: 'btn-b', x: 0.95, y: 0.42, w: 62, h: 62 },
        { id: 'btn_b', type: 'button', binding: 0x1000, label: 'B', btnClass: 'btn-a', x: 0.85, y: 0.56, w: 62, h: 62 }
    ],

    // 17. Twin-Stick Shooter (Move + Aim Dual Sticks)
    twinstick: [
        { id: 'lb', type: 'button', binding: 0x0100, label: 'DASH (LB)', x: 0.16, y: 0.10, w: 120, h: 46, shape: 'shoulder' },
        { id: 'start', type: 'button', binding: 0x0010, label: 'PAUSE', x: 0.50, y: 0.15, w: 86, h: 38, shape: 'pill' },
        { id: 'rb', type: 'button', binding: 0x0200, label: 'BOMB (RB)', x: 0.84, y: 0.10, w: 120, h: 46, shape: 'shoulder' },

        { id: 'ls', type: 'joystick', binding: 'left', x: 0.20, y: 0.55, w: 180, h: 180 },
        { id: 'lt', type: 'trigger', binding: 'LT', label: 'SPECIAL (LT)', x: 0.42, y: 0.82, w: 100, h: 44, shape: 'shoulder' },
        { id: 'rt', type: 'trigger', binding: 'RT', label: 'SUPER (RT)', x: 0.58, y: 0.82, w: 100, h: 44, shape: 'shoulder' },
        { id: 'rs', type: 'joystick', binding: 'right', x: 0.80, y: 0.55, w: 180, h: 180 }
    ],

    // 18. Flight Sim / HOTAS (Throttle + 3D Stick + Rudder)
    hotas_flight: [
        { id: 'start', type: 'button', binding: 0x0010, label: 'SYSTEM MENU', x: 0.50, y: 0.15, w: 120, h: 38, shape: 'pill' },
        { id: 'guns', type: 'button', binding: 0x1000, label: 'GUNS (A)', btnClass: 'btn-a', x: 0.65, y: 0.18, w: 88, h: 46, shape: 'pill' },
        { id: 'missiles', type: 'button', binding: 0x2000, label: 'MISSILES (B)', btnClass: 'btn-b', x: 0.82, y: 0.18, w: 106, h: 46, shape: 'pill' },

        // Left Side: Vertical Throttle & Yaw Rudder
        { id: 'throttle', type: 'pedal', binding: 'RT', label: 'THROTTLE', x: 0.14, y: 0.50, w: 100, h: 220 },
        { id: 'rudder_l', type: 'button', binding: 0x0004, label: 'YAW ◄', x: 0.08, y: 0.88, w: 80, h: 42, shape: 'pill' },
        { id: 'rudder_r', type: 'button', binding: 0x0008, label: '► YAW', x: 0.20, y: 0.88, w: 80, h: 42, shape: 'pill' },

        // Center Air Brake
        { id: 'brake', type: 'trigger', binding: 'LT', label: 'AIR BRAKE', x: 0.50, y: 0.84, w: 130, h: 44, shape: 'shoulder' },

        // Right Side: 3D Flight Stick & Countermeasures
        { id: 'stick', type: 'joystick', binding: 'left', x: 0.75, y: 0.58, w: 175, h: 175 },
        { id: 'flares', type: 'button', binding: 0x4000, label: 'FLARES (X)', btnClass: 'btn-x', x: 0.92, y: 0.58, w: 72, h: 54, shape: 'action' }
    ],

    // 19. MMO / RPG Action Bar
    mmo_action: [
        { id: 'target', type: 'button', binding: 0x0100, label: 'TAB TARGET', x: 0.16, y: 0.10, w: 120, h: 44, shape: 'shoulder' },
        { id: 'map', type: 'button', binding: 0x0020, label: 'MAP (M)', x: 0.42, y: 0.15, w: 80, h: 36, shape: 'pill' },
        { id: 'start', type: 'button', binding: 0x0010, label: 'MENU (ESC)', x: 0.58, y: 0.15, w: 94, h: 36, shape: 'pill' },
        { id: 'potion', type: 'button', binding: 0x0200, label: 'POTION (RB)', x: 0.84, y: 0.10, w: 120, h: 44, shape: 'shoulder' },

        // Left: Movement Stick + Jump
        { id: 'ls', type: 'joystick', binding: 'left', x: 0.18, y: 0.55, w: 160, h: 160 },
        { id: 'jump', type: 'button', binding: 0x1000, label: 'JUMP', btnClass: 'btn-a', x: 0.35, y: 0.75, w: 74, h: 54, shape: 'action' },

        // Right: 2x3 Quick Action Bar + Attack Buttons
        { id: 'act_1', type: 'button', binding: 0x4000, label: '1 (X)', btnClass: 'btn-x', x: 0.60, y: 0.40, w: 60, h: 60, shape: 'action' },
        { id: 'act_2', type: 'button', binding: 0x8000, label: '2 (Y)', btnClass: 'btn-y', x: 0.72, y: 0.40, w: 60, h: 60, shape: 'action' },
        { id: 'act_3', type: 'button', binding: 0x2000, label: '3 (B)', btnClass: 'btn-b', x: 0.84, y: 0.40, w: 60, h: 60, shape: 'action' },
        { id: 'act_4', type: 'button', binding: 0x0040, label: '4 (L3)', x: 0.60, y: 0.66, w: 60, h: 60, shape: 'action' },
        { id: 'act_5', type: 'button', binding: 0x0080, label: '5 (R3)', x: 0.72, y: 0.66, w: 60, h: 60, shape: 'action' },
        { id: 'act_6', type: 'trigger', binding: 'RT', label: '6 (RT)', x: 0.84, y: 0.66, w: 60, h: 60, shape: 'action' }
    ],

    // 20. Diagnostic & Debug Suite (Showcasing All Possible Controller Buttons & Inputs)
    debug_all: [
        // Top Shoulders & Triggers Row
        { id: 'dbg_lt', type: 'trigger', binding: 'LT', label: 'LT (0-255)', x: 0.08, y: 0.08, w: 86, h: 40, shape: 'shoulder' },
        { id: 'dbg_lb', type: 'button', binding: 0x0100, label: 'LB (0x0100)', x: 0.20, y: 0.08, w: 88, h: 40, shape: 'shoulder' },
        { id: 'dbg_back', type: 'button', binding: 0x0020, label: 'BACK\n0x0020', x: 0.32, y: 0.10, w: 64, h: 40, shape: 'pill' },
        { id: 'dbg_guide', type: 'button', binding: 0x0400, label: 'GUIDE\n0x0400', x: 0.44, y: 0.15, w: 64, h: 40, shape: 'pill' },
        { id: 'dbg_capture', type: 'button', binding: 0x0800, label: 'SHARE\n0x0800', x: 0.56, y: 0.15, w: 64, h: 40, shape: 'pill' },
        { id: 'dbg_start', type: 'button', binding: 0x0010, label: 'START\n0x0010', x: 0.68, y: 0.10, w: 64, h: 40, shape: 'pill' },
        { id: 'dbg_rb', type: 'button', binding: 0x0200, label: 'RB (0x0200)', x: 0.80, y: 0.08, w: 88, h: 40, shape: 'shoulder' },
        { id: 'dbg_rt', type: 'trigger', binding: 'RT', label: 'RT (0-255)', x: 0.92, y: 0.08, w: 86, h: 40, shape: 'shoulder' },

        // Center Touchpad Diagnostic Zone
        { id: 'dbg_touchpad', type: 'touchpad', label: 'TOUCHPAD & MOUSE DIAGNOSTIC SURFACE', x: 0.50, y: 0.40, w: 240, h: 100 },

        // Left Controls: Left Stick (with L3 click) + D-Pad
        { id: 'dbg_ls', type: 'joystick', binding: 'left', x: 0.13, y: 0.42, w: 135, h: 135 },
        { id: 'dbg_l3', type: 'button', binding: 0x0040, label: 'L3 Click (0x0040)', x: 0.13, y: 0.72, w: 100, h: 34, shape: 'pill' },
        { id: 'dbg_dpad', type: 'dpad', x: 0.31, y: 0.66, w: 135, h: 135 },

        // Right Controls: Diamond Face Buttons + Right Stick (with R3 click)
        { id: 'dbg_y', type: 'button', binding: 0x8000, label: 'Y (0x8000)', btnClass: 'btn-y', x: 0.68, y: 0.53, w: 56, h: 56 },
        { id: 'dbg_x', type: 'button', binding: 0x4000, label: 'X (0x4000)', btnClass: 'btn-x', x: 0.59, y: 0.67, w: 56, h: 56 },
        { id: 'dbg_b', type: 'button', binding: 0x2000, label: 'B (0x2000)', btnClass: 'btn-b', x: 0.77, y: 0.67, w: 56, h: 56 },
        { id: 'dbg_a', type: 'button', binding: 0x1000, label: 'A (0x1000)', btnClass: 'btn-a', x: 0.68, y: 0.81, w: 56, h: 56 },

        { id: 'dbg_rs', type: 'joystick', binding: 'right', x: 0.89, y: 0.42, w: 135, h: 135 },
        { id: 'dbg_r3', type: 'button', binding: 0x0080, label: 'R3 Click (0x0080)', x: 0.89, y: 0.72, w: 100, h: 34, shape: 'pill' }
    ]
};

// Preset Aliases for Game Detection & Auto-Profiles
LAYOUT_PRESETS.xbox360 = LAYOUT_PRESETS.xbox;
LAYOUT_PRESETS.ps = LAYOUT_PRESETS.playstation;
LAYOUT_PRESETS.ps4 = LAYOUT_PRESETS.playstation;
LAYOUT_PRESETS.ps5 = LAYOUT_PRESETS.playstation;
LAYOUT_PRESETS.switch = LAYOUT_PRESETS.switch_pro;
LAYOUT_PRESETS.flight = LAYOUT_PRESETS.hotas_flight;
LAYOUT_PRESETS.mmo = LAYOUT_PRESETS.mmo_action;
LAYOUT_PRESETS.debug = LAYOUT_PRESETS.debug_all;
LAYOUT_PRESETS.nes = LAYOUT_PRESETS.nes_retro;
LAYOUT_PRESETS.saturn = LAYOUT_PRESETS.sega_saturn;
LAYOUT_PRESETS.sega = LAYOUT_PRESETS.sega_saturn;
LAYOUT_PRESETS.ps1 = LAYOUT_PRESETS.ps1_classic;
LAYOUT_PRESETS.ds = LAYOUT_PRESETS.ds_3ds;
LAYOUT_PRESETS['3ds'] = LAYOUT_PRESETS.ds_3ds;

