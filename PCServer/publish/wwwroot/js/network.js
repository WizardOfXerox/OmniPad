/**
 * OmniPad Network Client
 * Binary WebSocket transport matching the 20-byte XINPUT frame specification.
 */
class NetworkClient {
    constructor() {
        this.socket = null;
        this.padSlot = 0;
        this.sequence = 0;
        this.isConnected = false;
        this.onRumble = null;
        this.onStatusChange = null;
        this.onSlotStatus = null;
        this.onSwapPrompt = null;
        this.onSwapDeclined = null;
        this.onSlotChanged = null;

        // Reusable 20-byte packet buffer
        this.packetBuffer = new ArrayBuffer(20);
        this.packetView = new DataView(this.packetBuffer);

        // Header fixed bytes
        this.packetView.setUint8(0, 0xDA); // Magic
        this.packetView.setUint8(1, 0x01); // Version
        this.packetView.setUint8(2, 0x01); // MSG_INPUT

        // Latency measurement
        this.lastPingSent = 0;
        this.latencyMs = 0;

        // Persistent per-tab session ID (survives page refresh F5, unique per tab)
        let sid = null;
        try {
            sid = sessionStorage.getItem('omnipad_session_id');
            if (!sid) {
                sid = 'pad_' + Math.random().toString(36).substring(2, 11) + '_' + Date.now().toString(36);
                sessionStorage.setItem('omnipad_session_id', sid);
            }
        } catch {
            sid = 'pad_' + Math.random().toString(36).substring(2, 11);
        }
        this.sessionId = sid;

        // Clean disconnect on tab close, reload, or navigation
        const handleUnload = () => {
            if (this.socket && this.socket.readyState === WebSocket.OPEN) {
                try {
                    // Send MsgBye (0x04)
                    const bye = new Uint8Array([0xDA, 0x01, 0x04, this.padSlot]);
                    this.socket.send(bye);
                    this.socket.close(1000, "Unload");
                } catch { }
            }
        };
        window.addEventListener('beforeunload', handleUnload);
        window.addEventListener('pagehide', handleUnload);
    }

    connect() {
        if (this.socket) {
            if (this.socket.readyState === WebSocket.OPEN || this.socket.readyState === WebSocket.CONNECTING) {
                return; // Already connected or connecting
            }
            try { this.socket.close(); } catch { }
        }

        const protocol = location.protocol === 'https:' ? 'wss:' : 'ws:';
        const wsUrl = `${protocol}//${location.host}/ws?sid=${encodeURIComponent(this.sessionId)}`;

        this.socket = new WebSocket(wsUrl);
        this.socket.binaryType = 'arraybuffer';

        this.socket.onopen = () => {
            this.isConnected = true;
            this.notifyStatus('Connected', true);
            this.startPingLoop();
        };

        this.socket.onclose = () => {
            if (this.pingInterval) clearInterval(this.pingInterval);
            this.isConnected = false;
            this.notifyStatus('Disconnected', false);
            if (this.reconnectTimeout) clearTimeout(this.reconnectTimeout);
            this.reconnectTimeout = setTimeout(() => this.connect(), 1500); // Auto-reconnect
        };

        this.socket.onerror = () => {
            try { this.socket.close(); } catch { }
        };

        this.socket.onmessage = (event) => {
            if (event.data instanceof ArrayBuffer) {
                this.handleBinaryMessage(new DataView(event.data));
            }
        };
    }

    reconnectImmediately() {
        if (this.reconnectTimeout) clearTimeout(this.reconnectTimeout);
        if (this.socket) {
            try { this.socket.close(); } catch { }
            this.socket = null;
        }
        this.connect();
    }

    handleBinaryMessage(view) {
        if (view.byteLength < 4) return;
        const magic = view.getUint8(0);
        const ver = view.getUint8(1);
        const type = view.getUint8(2);

        if (magic !== 0xDA || ver !== 1) return;

        // WELCOME message: captures assigned pad slot
        if (type === 0x03) {
            const oldSlot = this.padSlot;
            this.padSlot = view.getUint8(3);
            this.packetView.setUint8(3, this.padSlot);
            const slotText = this.padSlot === 0xFF ? 'Full' : `P${this.padSlot + 1}`;
            const slotBadgeText = document.getElementById('slot-badge-text');
            if (slotBadgeText) {
                slotBadgeText.textContent = slotText;
            } else {
                const slotEl = document.getElementById('slot-badge');
                if (slotEl) slotEl.textContent = slotText;
            }
            const topbarSlot = document.getElementById('topbar-slot-text');
            if (topbarSlot) topbarSlot.textContent = `Slot: ${slotText}`;

            if (this.onSlotChanged && oldSlot !== this.padSlot) {
                this.onSlotChanged(this.padSlot);
            }
        }

        // PONG message (0x08): RTT latency feedback
        if (type === 0x08) {
            if (this.lastPingSent > 0) {
                this.latencyMs = Math.max(1, Math.round(performance.now() - this.lastPingSent));
                const pingEl = document.getElementById('ping-text');
                if (pingEl) pingEl.textContent = `${this.latencyMs} ms`;
            }
        }

        // RUMBLE message: (0x05, slot, largeMotor, smallMotor)
        if (type === 0x05 && view.byteLength >= 6) {
            const largeMotor = view.getUint8(4);
            const smallMotor = view.getUint8(5);
            if (this.onRumble) {
                this.onRumble(largeMotor, smallMotor);
            }
        }

        // SLOT_STATUS message (0x09, slotCount, s0, s1, ..., sN)
        if (type === 0x09 && view.byteLength >= 4) {
            const count = view.getUint8(3);
            const statuses = [];
            for (let i = 0; i < count && (4 + i) < view.byteLength; i++) {
                statuses.push(view.getUint8(4 + i));
            }
            if (this.onSlotStatus) {
                this.onSlotStatus(statuses);
            }
        }

        // SWAP_PROMPT message (0x0C, fromSlot)
        if (type === 0x0C && view.byteLength >= 4) {
            const fromSlot = view.getUint8(3);
            if (this.onSwapPrompt) {
                this.onSwapPrompt(fromSlot);
            }
        }

        // SWAP_DECLINED message (0x0E, targetSlot)
        if (type === 0x0E && view.byteLength >= 4) {
            const targetSlot = view.getUint8(3);
            if (this.onSwapDeclined) {
                this.onSwapDeclined(targetSlot);
            }
        }
    }

    requestSlotSwitch(targetSlot) {
        if (!this.isConnected || !this.socket || this.socket.readyState !== WebSocket.OPEN) return;
        const pkt = new Uint8Array([0xDA, 0x01, 0x0A, targetSlot]);
        this.socket.send(pkt);
    }

    requestSlotSwap(targetSlot) {
        if (!this.isConnected || !this.socket || this.socket.readyState !== WebSocket.OPEN) return;
        const pkt = new Uint8Array([0xDA, 0x01, 0x0B, targetSlot]);
        this.socket.send(pkt);
    }

    respondToSwap(requesterSlot, accepted) {
        if (!this.isConnected || !this.socket || this.socket.readyState !== WebSocket.OPEN) return;
        const pkt = new Uint8Array([0xDA, 0x01, 0x0D, requesterSlot, accepted ? 1 : 0]);
        this.socket.send(pkt);
    }

    sendInput(state) {
        if (!this.isConnected || !this.socket || this.socket.readyState !== WebSocket.OPEN) return;

        // Sequence (u32 little-endian)
        this.sequence = (this.sequence + 1) >>> 0;
        this.packetView.setUint32(4, this.sequence, true);

        // Buttons (u16 little-endian)
        this.packetView.setUint16(8, state.buttons, true);

        // Triggers (u8, u8)
        this.packetView.setUint8(10, state.leftTrigger);
        this.packetView.setUint8(11, state.rightTrigger);

        // Axes (i16 little-endian, positive Y is up)
        this.packetView.setInt16(12, state.thumbLX, true);
        this.packetView.setInt16(14, state.thumbLY, true);
        this.packetView.setInt16(16, state.thumbRX, true);
        this.packetView.setInt16(18, state.thumbRY, true);

        this.socket.send(this.packetBuffer);
    }

    startPingLoop() {
        if (this.pingInterval) clearInterval(this.pingInterval);
        this.pingInterval = setInterval(() => {
            if (this.isConnected && this.socket && this.socket.readyState === WebSocket.OPEN) {
                this.lastPingSent = performance.now();
                // Send dummy 4-byte ping packet
                const ping = new Uint8Array([0xDA, 0x01, 0x07, this.padSlot]);
                this.socket.send(ping);
            }
        }, 1000);
    }

    notifyStatus(text, connected) {
        if (this.onStatusChange) this.onStatusChange(text, connected);
    }
}
