/**
 * OmniPad Visual Layout Customizer
 * Interactive drag-and-drop, scaling, multi-modal spawner, remapping inspector, and profile export/import.
 */
class LayoutCustomizer {
    constructor(app) {
        this.app = app;
        this.isEditing = false;
        this.selectedElement = null;
        this.selectedData = null;

        this.initToolbarListeners();
        this.initInspectorModal();
    }

    toggleEditMode() {
        this.isEditing = !this.isEditing;
        document.body.classList.toggle('editing', this.isEditing);
        const toolbar = document.getElementById('edit-toolbar');
        if (toolbar) toolbar.classList.toggle('hidden', !this.isEditing);

        if (!this.isEditing) {
            this.deselect();
        }
    }

    initToolbarListeners() {
        const on = (id, evt, fn) => {
            const el = document.getElementById(id);
            if (el) el.addEventListener(evt, fn);
        };

        on('btn-edit-mode', 'click', () => this.toggleEditMode());
        on('btn-exit-edit', 'click', () => this.toggleEditMode());

        on('btn-save-layout', 'click', () => {
            this.app.saveCurrentProfile();
            this.app.showToast('Custom layout saved!');
            this.toggleEditMode();
        });

        on('btn-save-as-new', 'click', () => {
            this.app.saveAsNewProfile();
        });

        on('btn-reset-layout', 'click', () => {
            if (confirm('Reset layout to factory preset?')) {
                this.app.resetCurrentProfile();
            }
        });

        // Add Control Dropdown Spawner
        const selectAdd = document.getElementById('select-add-control');
        if (selectAdd) {
            selectAdd.addEventListener('change', (e) => {
                const val = e.target.value;
                if (!val) return;
                this.spawnControl(val);
                selectAdd.value = '';
                try { selectAdd.blur(); } catch (e) {}
            });
        }

        // Delete Element
        on('btn-delete-element', 'click', () => {
            if (!this.selectedData) {
                alert('Please select an element first.');
                return;
            }
            if (confirm('Delete ' + (this.selectedData.label || this.selectedData.type) + '?')) {
                this.deleteSelected();
            }
        });

        // Inspect & Remap Element
        on('btn-inspect-element', 'click', () => {
            if (!this.selectedData) {
                alert('Please tap or select an element first to inspect.');
                return;
            }
            this.openInspector(this.selectedData);
        });

        // Toolbar Export & Import
        on('btn-toolbar-export', 'click', () => {
            this.app.openImportExportModal('export');
        });

        on('btn-toolbar-import', 'click', () => {
            this.app.openImportExportModal('import');
        });

        // Scale Slider
        on('comp-scale-slider', 'input', (e) => {
            if (this.selectedData && this.selectedElement) {
                const scale = e.target.value / 100.0;
                this.selectedData.scale = scale;
                this.selectedElement.style.setProperty('--elem-scale', scale.toString());
            }
        });

        // D-pad Spacing Slider
        on('dpad-spacing-slider', 'input', (e) => {
            if (this.selectedData && this.selectedElement && this.selectedData.type === 'dpad') {
                const spacing = parseInt(e.target.value, 10);
                this.selectedData.spacing = spacing;
                this.selectedElement.style.setProperty('--dpad-gap', spacing + 'px');
            }
        });

        // D-pad Button Size Slider
        on('dpad-btnsize-slider', 'input', (e) => {
            if (this.selectedData && this.selectedElement && this.selectedData.type === 'dpad') {
                const btnScale = e.target.value / 100.0;
                this.selectedData.btnScale = btnScale;
                this.selectedElement.style.setProperty('--dpad-btn-scale', btnScale.toString());
            }
        });

        // D-pad Background Disc Toggle
        on('dpad-bg-toggle', 'click', () => {
            if (this.selectedData && this.selectedElement && this.selectedData.type === 'dpad') {
                const currentShow = this.selectedData.showBg !== false;
                const nextShow = !currentShow;
                this.selectedData.showBg = nextShow;
                this.selectedElement.classList.toggle('dpad-no-bg', !nextShow);
                const bgToggle = document.getElementById('dpad-bg-toggle');
                if (bgToggle) bgToggle.textContent = nextShow ? 'Disc: ON' : 'Disc: OFF';
            }
        });
    }

    spawnControl(kind) {
        const timestamp = Date.now();
        let newControl = null;

        switch (kind) {
            case 'btn_standard':
                newControl = {
                    id: 'btn_' + timestamp,
                    type: 'button',
                    binding: 0x1000, // A
                    label: 'EXTRA',
                    x: 0.5,
                    y: 0.5,
                    w: 56,
                    h: 56
                };
                break;
            case 'btn_paddle':
                newControl = {
                    id: 'paddle_' + timestamp,
                    type: 'paddle',
                    binding: 'paddle_p1',
                    label: 'P1',
                    x: 0.5,
                    y: 0.5,
                    w: 68,
                    h: 42
                };
                break;
            case 'btn_toggle':
                newControl = {
                    id: 'toggle_' + timestamp,
                    type: 'toggle',
                    behavior: 'latch',
                    binding: 0x0040, // LS Click
                    label: 'SPRINT',
                    x: 0.5,
                    y: 0.5,
                    w: 64,
                    h: 44
                };
                break;
            case 'btn_turbo':
                newControl = {
                    id: 'turbo_' + timestamp,
                    type: 'turbo',
                    behavior: 'turbo',
                    binding: 0x1000, // A
                    label: 'TURBO',
                    x: 0.5,
                    y: 0.5,
                    w: 62,
                    h: 46
                };
                break;
            case 'btn_hold_dual':
                newControl = {
                    id: 'hold_' + timestamp,
                    type: 'hold_dual',
                    behavior: 'hold_dual',
                    binding: 0x4000, // X
                    secondaryBinding: 0x8000, // Y
                    label: 'X/Y',
                    x: 0.5,
                    y: 0.5,
                    w: 58,
                    h: 58
                };
                break;
            case 'btn_shoulder':
                newControl = {
                    id: 'bumper_' + timestamp,
                    type: 'shoulder',
                    binding: 0x0100, // LB
                    label: 'LB',
                    x: 0.5,
                    y: 0.5,
                    w: 78,
                    h: 40
                };
                break;
            case 'tp_scroll':
                newControl = {
                    id: 'scroll_' + timestamp,
                    type: 'touchpad_scroll',
                    label: 'SCROLL',
                    x: 0.5,
                    y: 0.5,
                    w: 56,
                    h: 120
                };
                break;
            case 'tp_dpad':
                newControl = {
                    id: 'tpdpad_' + timestamp,
                    type: 'touchpad_dpad',
                    label: 'D-PAD TOUCH',
                    x: 0.5,
                    y: 0.5,
                    w: 120,
                    h: 120
                };
                break;
            case 'tp_abxy':
                newControl = {
                    id: 'tpabxy_' + timestamp,
                    type: 'touchpad_abxy',
                    label: 'ABXY TOUCH',
                    x: 0.5,
                    y: 0.5,
                    w: 120,
                    h: 120
                };
                break;
            case 'tp_mouse':
                newControl = {
                    id: 'tpmouse_' + timestamp,
                    type: 'touchpad',
                    label: 'TRACKPAD',
                    x: 0.5,
                    y: 0.5,
                    w: 160,
                    h: 110
                };
                break;
            case 'ctrl_left_stick':
                newControl = {
                    id: 'stick_l_' + timestamp,
                    type: 'joystick',
                    stick: 'left',
                    label: 'L',
                    x: 0.5,
                    y: 0.5,
                    w: 120,
                    h: 120
                };
                break;
            case 'ctrl_right_stick':
                newControl = {
                    id: 'stick_r_' + timestamp,
                    type: 'joystick',
                    stick: 'right',
                    label: 'R',
                    x: 0.5,
                    y: 0.5,
                    w: 120,
                    h: 120
                };
                break;
            case 'ctrl_dpad':
                newControl = {
                    id: 'dpad_' + timestamp,
                    type: 'dpad',
                    label: 'D-PAD',
                    x: 0.5,
                    y: 0.5,
                    w: 140,
                    h: 140,
                    spacing: 14,
                    btnScale: 1.0,
                    showBg: true
                };
                break;
            case 'ctrl_combo':
                newControl = {
                    id: 'combo_' + timestamp,
                    type: 'combo',
                    combo: ['LB', 'RB'],
                    label: 'LB+RB',
                    x: 0.5,
                    y: 0.5,
                    w: 74,
                    h: 44
                };
                break;
            case 'ctrl_macro':
                newControl = {
                    id: 'macro_' + timestamp,
                    type: 'macro',
                    macroId: 'macro_ewgf',
                    label: '⚡ EWGF',
                    x: 0.5,
                    y: 0.5,
                    w: 80,
                    h: 46
                };
                break;
            case 'fn_mute':
                newControl = {
                    id: 'mute_' + timestamp,
                    type: 'button',
                    behavior: 'latch',
                    binding: 'fn_mute',
                    label: '🔇 MUTE',
                    x: 0.5,
                    y: 0.5,
                    w: 60,
                    h: 42
                };
                break;
            case 'fn_recenter':
                newControl = {
                    id: 'recenter_' + timestamp,
                    type: 'button',
                    binding: 'fn_recenter',
                    label: '🎯 RECENTER',
                    x: 0.5,
                    y: 0.5,
                    w: 76,
                    h: 40
                };
                break;
            default:
                break;
        }

        if (newControl) {
            this.app.currentLayout.push(newControl);
            this.app.renderLayout();
            this.app.showToast('Added: ' + newControl.label);
            setTimeout(() => {
                const el = document.getElementById(newControl.id);
                if (el) this.select(el, newControl);
            }, 50);
        }
    }

    deleteSelected() {
        if (!this.selectedData) return;
        const targetId = this.selectedData.id;
        this.app.currentLayout = this.app.currentLayout.filter(item => item.id !== targetId);
        this.deselect();
        this.app.renderLayout();
        this.app.showToast('Element removed');
    }

    initInspectorModal() {
        const on = (id, evt, fn) => {
            const el = document.getElementById(id);
            if (el) el.addEventListener(evt, fn);
        };

        const modal = document.getElementById('modal-button-inspector');
        const closeModal = () => {
            if (modal) modal.classList.add('hidden');
            document.body.classList.remove('modal-open');
        };

        on('btn-close-inspector', 'click', closeModal);
        on('backdrop-button-inspector', 'click', closeModal);

        on('insp-elem-behavior', 'change', (e) => {
            const secGroup = document.getElementById('insp-secondary-group');
            if (secGroup) {
                secGroup.style.display = e.target.value === 'hold_dual' ? 'block' : 'none';
            }
        });

        on('btn-inspector-delete', 'click', () => {
            closeModal();
            this.deleteSelected();
        });

        on('btn-inspector-apply', 'click', () => {
            if (!this.selectedData) return;

            const labelInput = document.getElementById('insp-elem-label');
            const behaviorSel = document.getElementById('insp-elem-behavior');
            const bindingSel = document.getElementById('insp-elem-binding');
            const secSel = document.getElementById('insp-elem-secondary');
            const comboInput = document.getElementById('insp-elem-combo');
            const macroSel = document.getElementById('insp-elem-macro');
            const wInput = document.getElementById('insp-elem-w');
            const hInput = document.getElementById('insp-elem-h');

            if (labelInput) this.selectedData.label = labelInput.value.trim();
            if (behaviorSel) this.selectedData.behavior = behaviorSel.value;

            if (bindingSel) {
                const rawVal = bindingSel.value;
                if (rawVal.startsWith('0x')) {
                    this.selectedData.binding = parseInt(rawVal, 16);
                } else {
                    this.selectedData.binding = rawVal;
                }
            }

            if (secSel && this.selectedData.behavior === 'hold_dual') {
                const rawSec = secSel.value;
                if (rawSec.startsWith('0x')) {
                    this.selectedData.secondaryBinding = parseInt(rawSec, 16);
                } else {
                    this.selectedData.secondaryBinding = rawSec;
                }
            }

            if (comboInput && this.selectedData.type === 'combo') {
                const comboStr = comboInput.value.trim();
                this.selectedData.combo = comboStr.split(',').map(s => s.trim().toUpperCase());
            }

            if (macroSel && this.selectedData.type === 'macro') {
                this.selectedData.macroId = macroSel.value;
            }

            if (wInput && parseInt(wInput.value, 10) > 0) {
                this.selectedData.w = parseInt(wInput.value, 10);
            }
            if (hInput && parseInt(hInput.value, 10) > 0) {
                this.selectedData.h = parseInt(hInput.value, 10);
            }

            closeModal();
            this.app.renderLayout();
            this.app.showToast('Control updated');
            setTimeout(() => {
                const el = document.getElementById(this.selectedData.id);
                if (el) this.select(el, this.selectedData);
            }, 50);
        });
    }

    openInspector(itemData) {
        const modal = document.getElementById('modal-button-inspector');
        if (!modal) return;

        const idBadge = document.getElementById('insp-elem-id');
        const labelInput = document.getElementById('insp-elem-label');
        const behaviorSel = document.getElementById('insp-elem-behavior');
        const bindingSel = document.getElementById('insp-elem-binding');
        const secGroup = document.getElementById('insp-secondary-group');
        const secSel = document.getElementById('insp-elem-secondary');
        const comboGroup = document.getElementById('insp-combo-group');
        const comboInput = document.getElementById('insp-elem-combo');
        const macroGroup = document.getElementById('insp-macro-group');
        const macroSel = document.getElementById('insp-elem-macro');
        const wInput = document.getElementById('insp-elem-w');
        const hInput = document.getElementById('insp-elem-h');

        if (idBadge) idBadge.textContent = itemData.id;
        if (labelInput) labelInput.value = itemData.label || '';
        if (behaviorSel) behaviorSel.value = itemData.behavior || 'momentary';

        if (bindingSel) {
            if (typeof itemData.binding === 'number') {
                bindingSel.value = '0x' + itemData.binding.toString(16).padStart(4, '0');
            } else if (itemData.binding) {
                bindingSel.value = itemData.binding;
            }
        }

        if (secGroup) {
            secGroup.style.display = itemData.behavior === 'hold_dual' ? 'block' : 'none';
        }
        if (secSel && itemData.secondaryBinding) {
            if (typeof itemData.secondaryBinding === 'number') {
                secSel.value = '0x' + itemData.secondaryBinding.toString(16).padStart(4, '0');
            } else {
                secSel.value = itemData.secondaryBinding;
            }
        }

        if (comboGroup) {
            comboGroup.style.display = itemData.type === 'combo' ? 'block' : 'none';
        }
        if (comboInput && itemData.combo) {
            comboInput.value = itemData.combo.join(',');
        }

        if (macroGroup) {
            macroGroup.style.display = itemData.type === 'macro' ? 'block' : 'none';
        }
        if (macroSel && itemData.macroId) {
            macroSel.value = itemData.macroId;
        }

        if (wInput) wInput.value = itemData.w || 56;
        if (hInput) hInput.value = itemData.h || 56;

        modal.classList.remove('hidden');
        document.body.classList.add('modal-open');
    }

    makeDraggable(el, itemData) {
        let isDragging = false;
        let startX, startY;

        el.addEventListener('pointerdown', (e) => {
            if (!this.isEditing) return;
            e.stopPropagation();
            isDragging = true;
            el.setPointerCapture(e.pointerId);

            this.select(el, itemData);
            startX = e.clientX;
            startY = e.clientY;
        });

        el.addEventListener('pointermove', (e) => {
            if (!this.isEditing || !isDragging) return;
            e.stopPropagation();

            const container = document.getElementById('gamepad-container');
            const rect = container.getBoundingClientRect();

            const normX = Math.max(0.04, Math.min(0.96, (e.clientX - rect.left) / rect.width));
            const normY = Math.max(0.04, Math.min(0.96, (e.clientY - rect.top) / rect.height));

            itemData.x = normX;
            itemData.y = normY;

            el.style.left = (normX * 100) + '%';
            el.style.top = (normY * 100) + '%';
        });

        const stopDrag = (e) => {
            if (!this.isEditing) return;
            isDragging = false;
        };

        el.addEventListener('pointerup', stopDrag);
        el.addEventListener('pointercancel', stopDrag);
    }

    select(el, itemData) {
        this.deselect();
        this.selectedElement = el;
        this.selectedData = itemData;
        el.classList.add('selected');

        const slider = document.getElementById('comp-scale-slider');
        if (slider) {
            slider.value = Math.round((itemData.scale || 1.0) * 100);
        }

        const dpadCtrls = document.querySelectorAll('.dpad-only-ctrl');
        if (itemData.type === 'dpad') {
            dpadCtrls.forEach(c => c.style.display = 'inline-flex');
            const spacingSlider = document.getElementById('dpad-spacing-slider');
            if (spacingSlider) {
                spacingSlider.value = itemData.spacing !== undefined ? itemData.spacing : 14;
            }
            const btnSizeSlider = document.getElementById('dpad-btnsize-slider');
            if (btnSizeSlider) {
                btnSizeSlider.value = Math.round((itemData.btnScale || 1.0) * 100);
            }
            const bgToggle = document.getElementById('dpad-bg-toggle');
            if (bgToggle) {
                bgToggle.textContent = itemData.showBg !== false ? 'Disc: ON' : 'Disc: OFF';
            }
        } else {
            dpadCtrls.forEach(c => c.style.display = 'none');
        }
    }

    deselect() {
        if (this.selectedElement) {
            this.selectedElement.classList.remove('selected');
            this.selectedElement = null;
            this.selectedData = null;
        }
        const dpadCtrls = document.querySelectorAll('.dpad-only-ctrl');
        dpadCtrls.forEach(c => c.style.display = 'none');
    }
}
