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
        this.onShareLayoutPrompt = null;
        this.onShareLayoutDeclined = null;
        this.onShareLayoutAccepted = null;
        this.onSlotChanged = null;
        this.onProfileChange = null;
        this.onTransportChange = null;

        // Multi-transport auto-failover state
        this.currentTransport = 'unknown'; // 'wired' or 'wifi'
        this.currentHost = location.host || '127.0.0.1:27502';
        this.wiredProbeInterval = null;

        // Remember host if initially launched directly via WiFi IP
        if (location.hostname && location.hostname !== '127.0.0.1' && location.hostname !== 'localhost') {
            try {
                localStorage.setItem('omnipad_server_lan_ip', location.hostname);
            } catch (e) { }
        }

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
        } catch (e) {
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
                } catch (e) { }
            }
        };
        window.addEventListener('beforeunload', handleUnload);
        window.addEventListener('pagehide', handleUnload);
    }

    connect(targetHost) {
        if (targetHost) {
            this.currentHost = targetHost;
        }

        if (this.configuredMode === 'offline' || this.currentTransport === 'bluetooth') {
            return;
        }

        if (this.socket) {
            if (this.socket.readyState === WebSocket.OPEN || this.socket.readyState === WebSocket.CONNECTING) {
                return; // Already connected or connecting
            }
            try { this.socket.close(); } catch (e) { }
        }

        const protocol = location.protocol === 'https:' ? 'wss:' : 'ws:';
        const wsUrl = `${protocol}//${this.currentHost}/ws?sid=${encodeURIComponent(this.sessionId)}`;

        const isLoopback = this.currentHost.startsWith('127.0.0.1') || this.currentHost.startsWith('localhost');
        let isWired = false;
        if (window.OmniPadNative && typeof window.OmniPadNative.isUsbConnected === 'function') {
            isWired = window.OmniPadNative.isUsbConnected() && isLoopback;
        } else if (this.configuredMode === 'usb') {
            isWired = true;
        } else if (isLoopback && !navigator.userAgent.includes('Android')) {
            isWired = true;
        }

        const expectedTransport = isWired ? 'wired' : 'wifi';

        this.socket = new WebSocket(wsUrl);
        this.socket.binaryType = 'arraybuffer';

        this.socket.onopen = () => {
            this.isConnected = true;
            this.currentTransport = expectedTransport;
            this.notifyStatus(expectedTransport === 'wired' ? 'Wired USB Connected' : 'WiFi Connected', true);
            this.startPingLoop();

            if (this.onTransportChange) {
                this.onTransportChange(this.currentTransport, this.currentHost);
            }

            // Probe server for LAN IP to store for failover
            this.discoverLanIp();

            // When running on WiFi, monitor for USB re-plug in the background
            if (this.currentTransport === 'wifi') {
                this.startWiredProbeLoop();
            } else {
                this.stopWiredProbeLoop();
            }
        };

        this.socket.onclose = () => {
            if (this.pingInterval) clearInterval(this.pingInterval);
            this.isConnected = false;
            this.notifyStatus('Disconnected', false);

            // AUTO-FAILOVER: If wired loopback disconnected (e.g. cable unplugged), try known WiFi LAN IP
            let savedLanIp = null;
            try {
                savedLanIp = localStorage.getItem('omnipad_server_lan_ip');
            } catch (e) { }

            if (isLoopback && savedLanIp && savedLanIp !== '127.0.0.1' && savedLanIp !== 'localhost') {
                if (this.reconnectTimeout) clearTimeout(this.reconnectTimeout);
                this.currentHost = `${savedLanIp}:27502`;
                this.reconnectTimeout = setTimeout(() => this.connect(), 400);
                return;
            }

            if (this.configuredMode === 'offline' || this.currentTransport === 'bluetooth') {
                return;
            }

            if (this.reconnectTimeout) clearTimeout(this.reconnectTimeout);
            this.reconnectTimeout = setTimeout(() => this.connect(), 1500); // Auto-reconnect
        };

        this.socket.onerror = () => {
            try { this.socket.close(); } catch (e) { }
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
            try { this.socket.close(); } catch (e) { }
            this.socket = null;
        }
        this.connect();
    }

    discoverLanIp() {
        try {
            const infoUrl = 'http://' + this.currentHost + '/api/network/info';
            fetch(infoUrl)
                .then(res => res.json())
                .then(data => {
                    if (data && data.lanIps && data.lanIps.length > 0) {
                        for (let i = 0; i < data.lanIps.length; i++) {
                            const ip = data.lanIps[i];
                            if (ip && ip !== '127.0.0.1' && ip.indexOf('192.168.56.') !== 0 && ip.indexOf('169.254.') !== 0) {
                                this.discoveredLanIp = ip;
                                localStorage.setItem('omnipad_server_lan_ip', ip);
                                break;
                            }
                        }
                    }
                })
                .catch(function(e) { });
        } catch (e) { }
    }

    startWiredProbeLoop() {
        this.stopWiredProbeLoop();
        this.wiredProbeInterval = setInterval(() => {
            this.probeWiredConnection();
        }, 2500);
    }

    stopWiredProbeLoop() {
        if (this.wiredProbeInterval) {
            clearInterval(this.wiredProbeInterval);
            this.wiredProbeInterval = null;
        }
    }

    probeWiredConnection() {
        try {
            const probeUrl = 'http://127.0.0.1:27502/api/status?_t=' + Date.now();
            fetch(probeUrl, { method: 'GET', cache: 'no-store' })
                .then(res => {
                    if (res && res.ok) {
                        // Wired USB loopback is reachable! Upgrade to wired connection
                        this.stopWiredProbeLoop();
                        this.currentHost = '127.0.0.1:27502';
                        this.reconnectImmediately();
                    }
                })
                .catch(function(e) { });
        } catch (e) { }
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
            if (topbarSlot) topbarSlot.textContent = slotText;

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

        // ACTIVE_PROFILE message (0x13, len, ...name)
        if (type === 0x13 && view.byteLength >= 4) {
            const nameLen = view.getUint8(3);
            let name = '';
            for (let i = 0; i < nameLen && (4 + i) < view.byteLength; i++) {
                name += String.fromCharCode(view.getUint8(4 + i));
            }
            if (this.onProfileChange && name) {
                this.onProfileChange(name);
            }
        }

        // SHARE_LAYOUT_PROMPT message (0x15, fromSlot, lenLow, lenHigh, ...jsonBytes)
        if (type === 0x15 && view.byteLength >= 6) {
            const fromSlot = view.getUint8(3);
            const jsonLen = view.getUint8(4) | (view.getUint8(5) << 8);
            if (view.byteLength >= 6 + jsonLen) {
                try {
                    const u8 = new Uint8Array(view.buffer, view.byteOffset + 6, jsonLen);
                    let jsonStr = '';
                    if (typeof TextDecoder !== 'undefined') {
                        jsonStr = new TextDecoder('utf-8').decode(u8);
                    } else {
                        for (let i = 0; i < u8.length; i++) {
                            jsonStr += String.fromCharCode(u8[i]);
                        }
                    }
                    const layoutData = JSON.parse(jsonStr);
                    if (this.onShareLayoutPrompt) {
                        this.onShareLayoutPrompt(fromSlot, layoutData);
                    }
                } catch (e) {
                    console.error('[ShareLayout] Failed to parse incoming layout JSON:', e);
                }
            }
        }

        // SHARE_LAYOUT_DECLINED message (0x17, targetSlot, ...nameBytes)
        if (type === 0x17 && view.byteLength >= 4) {
            const targetSlot = view.getUint8(3);
            let layoutName = '';
            if (view.byteLength > 4) {
                const u8 = new Uint8Array(view.buffer, view.byteOffset + 4, view.byteLength - 4);
                try {
                    layoutName = typeof TextDecoder !== 'undefined' ? new TextDecoder('utf-8').decode(u8) : String.fromCharCode.apply(null, u8);
                } catch (e) { }
            }
            if (this.onShareLayoutDeclined) {
                this.onShareLayoutDeclined(targetSlot, layoutName);
            }
        }

        // SHARE_LAYOUT_ACCEPTED message (0x18, targetSlot, ...nameBytes)
        if (type === 0x18 && view.byteLength >= 4) {
            const targetSlot = view.getUint8(3);
            let layoutName = '';
            if (view.byteLength > 4) {
                const u8 = new Uint8Array(view.buffer, view.byteOffset + 4, view.byteLength - 4);
                try {
                    layoutName = typeof TextDecoder !== 'undefined' ? new TextDecoder('utf-8').decode(u8) : String.fromCharCode.apply(null, u8);
                } catch (e) { }
            }
            if (this.onShareLayoutAccepted) {
                this.onShareLayoutAccepted(targetSlot, layoutName);
            }
        }
    }

    sendMotion(motion) {
        if (!this.isConnected || !this.socket || this.socket.readyState !== WebSocket.OPEN) return;

        const buffer = new ArrayBuffer(36);
        const view = new DataView(buffer);
        view.setUint8(0, 0xDA);
        view.setUint8(1, 0x01);
        view.setUint8(2, 0x10); // MsgMotion
        view.setUint8(3, this.padSlot);

        // timestampUs (u64 little-endian: low 32-bit word at offset 4, high 32-bit word at offset 8)
        const nowUs = Math.round(performance.now() * 1000);
        const lowWord = (nowUs % 0x100000000) >>> 0;
        const highWord = Math.floor(nowUs / 0x100000000) >>> 0;
        view.setUint32(4, lowWord, true);
        view.setUint32(8, highWord, true);

        // 3-axis accel in G (f32 little-endian)
        view.setFloat32(12, motion.accelX || 0, true);
        view.setFloat32(16, motion.accelY || 0, true);
        view.setFloat32(20, motion.accelZ || 0, true);

        // 3-axis gyro in deg/s (f32 little-endian)
        view.setFloat32(24, motion.gyroX || 0, true);
        view.setFloat32(28, motion.gyroY || 0, true);
        view.setFloat32(32, motion.gyroZ || 0, true);

        this.socket.send(buffer);
    }

    sendTouchpad(tpState) {
        if (!this.isConnected || !this.socket || this.socket.readyState !== WebSocket.OPEN) return;

        const buffer = new ArrayBuffer(13);
        const view = new DataView(buffer);
        view.setUint8(0, 0xDA);
        view.setUint8(1, 0x01);
        view.setUint8(2, 0x11); // MsgTouchpad
        view.setUint8(3, this.padSlot);

        let flags = 0;
        if (tpState.clicked) flags |= 0x01;
        if (tpState.finger0 && tpState.finger0.isActive) flags |= 0x02;
        if (tpState.finger1 && tpState.finger1.isActive) flags |= 0x04;
        view.setUint8(4, flags);

        const f0X = (tpState.finger0 && tpState.finger0.isActive) ? Math.max(0, Math.min(1920, tpState.finger0.x)) : 0;
        const f0Y = (tpState.finger0 && tpState.finger0.isActive) ? Math.max(0, Math.min(942, tpState.finger0.y)) : 0;
        view.setUint16(5, f0X, true);
        view.setUint16(7, f0Y, true);

        const f1X = (tpState.finger1 && tpState.finger1.isActive) ? Math.max(0, Math.min(1920, tpState.finger1.x)) : 0;
        const f1Y = (tpState.finger1 && tpState.finger1.isActive) ? Math.max(0, Math.min(942, tpState.finger1.y)) : 0;
        view.setUint16(9, f1X, true);
        view.setUint16(11, f1Y, true);

        this.socket.send(buffer);
    }

    sendControllerType(type) {
        if (!this.isConnected || !this.socket || this.socket.readyState !== WebSocket.OPEN) return;
        const isDs4 = type === 'dualshock4' || type === 'ds4' || type === 'ps4';
        const buffer = new Uint8Array([0xDA, 0x01, 0x12, isDs4 ? 1 : 0]);
        this.socket.send(buffer);
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

    shareLayoutWithPlayer(targetSlot, layoutData) {
        if (!this.isConnected || !this.socket || this.socket.readyState !== WebSocket.OPEN) return false;
        try {
            const jsonStr = typeof layoutData === 'string' ? layoutData : JSON.stringify(layoutData);
            let jsonBytes;
            if (typeof TextEncoder !== 'undefined') {
                jsonBytes = new TextEncoder().encode(jsonStr);
            } else {
                jsonBytes = new Uint8Array(jsonStr.length);
                for (let i = 0; i < jsonStr.length; i++) {
                    jsonBytes[i] = jsonStr.charCodeAt(i) & 0xFF;
                }
            }
            const len = jsonBytes.length;
            const buffer = new ArrayBuffer(6 + len);
            const view = new DataView(buffer);
            view.setUint8(0, 0xDA);
            view.setUint8(1, 0x01);
            view.setUint8(2, 0x14); // MsgShareLayoutRequest
            view.setUint8(3, targetSlot); // targetSlot or 0xFF for broadcast
            view.setUint8(4, len & 0xFF);
            view.setUint8(5, (len >> 8) & 0xFF);
            new Uint8Array(buffer, 6).set(jsonBytes);
            this.socket.send(buffer);
            return true;
        } catch (e) {
            console.error('[ShareLayout] Failed to send layout:', e);
            return false;
        }
    }

    respondToLayoutShare(senderSlot, accepted, layoutName) {
        if (!this.isConnected || !this.socket || this.socket.readyState !== WebSocket.OPEN) return;
        try {
            let nameBytes = new Uint8Array(0);
            if (layoutName) {
                if (typeof TextEncoder !== 'undefined') {
                    nameBytes = new TextEncoder().encode(layoutName);
                } else {
                    nameBytes = new Uint8Array(layoutName.length);
                    for (let i = 0; i < layoutName.length; i++) {
                        nameBytes[i] = layoutName.charCodeAt(i) & 0xFF;
                    }
                }
            }
            const buffer = new ArrayBuffer(5 + nameBytes.length);
            const view = new DataView(buffer);
            view.setUint8(0, 0xDA);
            view.setUint8(1, 0x01);
            view.setUint8(2, 0x16); // MsgShareLayoutResponse
            view.setUint8(3, senderSlot);
            view.setUint8(4, accepted ? 1 : 0);
            if (nameBytes.length > 0) {
                new Uint8Array(buffer, 5).set(nameBytes);
            }
            this.socket.send(buffer);
        } catch (e) {
            console.error('[ShareLayout] Failed to respond to layout share:', e);
        }
    }

    sendInput(state) {
        if (this.currentTransport === 'bluetooth' || (!this.isConnected && this.configuredMode === 'bluetooth')) {
            if (window.OmniPadNative && typeof window.OmniPadNative.sendBluetoothReport === 'function') {
                window.OmniPadNative.sendBluetoothReport(
                    state.buttons || 0,
                    state.thumbLX || 0,
                    state.thumbLY || 0,
                    state.thumbRX || 0,
                    state.thumbRY || 0,
                    state.leftTrigger || 0,
                    state.rightTrigger || 0
                );
            }
            return;
        }

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

    enableBluetoothMode() {
        if (window.OmniPadNative && typeof window.OmniPadNative.startBluetoothHid === 'function') {
            const started = window.OmniPadNative.startBluetoothHid();
            if (started) {
                this.currentTransport = 'bluetooth';
                this.notifyStatus('Bluetooth: Initializing...', true);
            }
            return started;
        }
        return false;
    }

    setConnectivityMode(mode) {
        this.configuredMode = mode;
        if (mode === 'bluetooth') {
            if (this.socket) {
                try { this.socket.close(); } catch (e) { }
            }
            return this.enableBluetoothMode();
        }

        // Disable bluetooth if leaving bluetooth mode
        if (window.OmniPadNative && typeof window.OmniPadNative.stopBluetoothHid === 'function') {
            try { window.OmniPadNative.stopBluetoothHid(); } catch (e) { }
        }

        if (mode === 'offline') {
            if (this.socket) {
                try { this.socket.close(); } catch (e) { }
            }
            this.currentTransport = 'offline';
            this.isConnected = false;
            this.notifyStatus('Offline Standalone Mode', true);
            if (this.onTransportChange) this.onTransportChange('offline');
            return true;
        }

        if (mode === 'usb') {
            this.currentHost = '127.0.0.1:27502';
            this.reconnectImmediately();
            return true;
        }

        if (mode === 'wifi') {
            const savedLan = localStorage.getItem('omnipad_server_lan_ip');
            if (savedLan) {
                this.currentHost = `${savedLan}:27502`;
                this.reconnectImmediately();
            } else if (location.hostname && location.hostname !== '127.0.0.1' && location.hostname !== 'localhost') {
                this.currentHost = `${location.hostname}:27502`;
                this.reconnectImmediately();
            } else {
                this.notifyStatus('WiFi: Server IP needed in Settings', false);
            }
            return true;
        }

        // 'auto' mode
        const savedLan = localStorage.getItem('omnipad_server_lan_ip');
        let isWired = false;
        if (window.OmniPadNative && typeof window.OmniPadNative.isUsbConnected === 'function') {
            isWired = window.OmniPadNative.isUsbConnected();
        }
        if (isWired) {
            this.currentHost = '127.0.0.1:27502';
        } else if (savedLan) {
            this.currentHost = `${savedLan}:27502`;
        } else if (location.hostname && location.hostname !== '127.0.0.1' && location.hostname !== 'localhost') {
            this.currentHost = `${location.hostname}:27502`;
        }
        this.reconnectImmediately();
        return true;
    }

    onUsbConnected() {
        if (this.configuredMode !== 'wifi' && this.configuredMode !== 'bluetooth' && this.configuredMode !== 'offline') {
            this.currentTransport = 'wired';
            this.currentHost = '127.0.0.1:27502';
            this.reconnectImmediately();
            this.notifyStatus('Wired USB Connected', true);
            if (this.onTransportChange) {
                this.onTransportChange('wired', this.currentHost);
            }
        }
    }

    onUsbDisconnected() {
        if (this.currentTransport === 'wired') {
            this.currentTransport = 'wifi';
            const savedLan = localStorage.getItem('omnipad_server_lan_ip');
            if (savedLan) {
                this.currentHost = `${savedLan}:27502`;
            }
            this.reconnectImmediately();
            this.notifyStatus('WiFi Connected', true);
            if (this.onTransportChange) {
                this.onTransportChange('wifi', this.currentHost);
            }
        }
    }

    onBluetoothStatusChanged(status, hostName) {
        if (status === 'connected') {
            this.currentTransport = 'bluetooth';
            this.isConnected = true;
            this.notifyStatus(`Bluetooth: Connected (${hostName || 'Host'})`, true);
        } else if (status === 'ready') {
            this.currentTransport = 'bluetooth';
            this.notifyStatus('Bluetooth: Ready to Pair', true);
        } else if (status === 'disabled') {
            this.notifyStatus('Bluetooth: Disabled in Settings', false);
        } else if (status === 'unsupported') {
            this.notifyStatus('Bluetooth: Unsupported', false);
        } else if (status === 'disconnected') {
            if (this.currentTransport === 'bluetooth') {
                this.notifyStatus('Bluetooth: Disconnected', false);
            }
        }
        if (this.onTransportChange) {
            this.onTransportChange(this.currentTransport, hostName);
        }
        if (this.onBluetoothStateChange) {
            this.onBluetoothStateChange(status, hostName);
        }
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

