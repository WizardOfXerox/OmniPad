/**
 * OmniPad Visual Layout Customizer
 * Interactive drag-and-drop, scaling, custom button spawner, profile JSON export/import.
 */
class LayoutCustomizer {
    constructor(app) {
        this.app = app;
        this.isEditing = false;
        this.selectedElement = null;
        this.selectedData = null;

        this.initToolbarListeners();
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
            alert('Custom layout saved!');
            this.toggleEditMode();
        });

        on('btn-reset-layout', 'click', () => {
            if (confirm('Reset layout to factory preset?')) {
                this.app.resetCurrentProfile();
            }
        });

        // Add Button
        on('btn-add-button', 'click', () => {
            const label = prompt('Button label (e.g. EXTRA, C, Z):', 'EXTRA');
            if (label) {
                this.app.currentLayout.push({
                    id: 'custom_' + Date.now(),
                    type: 'button',
                    binding: 0x1000, // default A
                    label: label,
                    x: 0.5,
                    y: 0.5,
                    w: 58,
                    h: 58
                });
                this.app.renderLayout();
            }
        });

        // Add Combo Button
        on('btn-add-combo', 'click', () => {
            const comboStr = prompt('Buttons to combine (e.g. X,Y or A,B):', 'X,Y');
            if (comboStr) {
                const buttons = comboStr.split(',').map(s => s.trim().toUpperCase());
                this.app.currentLayout.push({
                    id: 'combo_' + Date.now(),
                    type: 'combo',
                    combo: buttons,
                    label: buttons.join('+'),
                    x: 0.5,
                    y: 0.5,
                    w: 64,
                    h: 50
                });
                this.app.renderLayout();
            }
        });

        // Add Macro Button
        on('btn-add-macro', 'click', () => {
            const macroId = prompt('Macro ID (e.g. macro_ewgf, macro_slide_cancel):', 'macro_ewgf');
            if (macroId) {
                this.app.currentLayout.push({
                    id: 'macro_' + Date.now(),
                    type: 'macro',
                    macroId: macroId,
                    label: '⚡ ' + macroId.replace('macro_', '').toUpperCase(),
                    x: 0.5,
                    y: 0.5,
                    w: 75,
                    h: 46
                });
                this.app.renderLayout();
            }
        });

        // Scale Slider
        on('comp-scale-slider', 'input', (e) => {
            if (this.selectedData && this.selectedElement) {
                const scale = e.target.value / 100.0;
                this.selectedData.scale = scale;
                this.selectedElement.style.setProperty('--elem-scale', scale.toString());
            }
        });

        // D-pad Spacing Slider (Controls gap from center / between buttons)
        on('dpad-spacing-slider', 'input', (e) => {
            if (this.selectedData && this.selectedElement && this.selectedData.type === 'dpad') {
                const spacing = parseInt(e.target.value);
                this.selectedData.spacing = spacing;
                this.selectedElement.style.setProperty('--dpad-gap', `${spacing}px`);
            }
        });

        // D-pad Button Size Slider (Controls size of individual directional buttons)
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

            const normX = Math.max(0.05, Math.min(0.95, (e.clientX - rect.left) / rect.width));
            const normY = Math.max(0.05, Math.min(0.95, (e.clientY - rect.top) / rect.height));

            itemData.x = normX;
            itemData.y = normY;

            el.style.left = `${normX * 100}%`;
            el.style.top = `${normY * 100}%`;
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
