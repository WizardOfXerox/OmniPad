/**
 * OmniPad Wireless Microphone Engine
 * Captures microphone audio from mobile device (16-bit PCM, 16kHz mono)
 * and streams it in real-time to PCServer (/ws/mic) for Discord, Steam Voice, and games.
 */
class MicEngine {
    constructor(networkClient) {
        this.network = networkClient;
        this.isStreaming = false;
        this.isMuted = false;
        this.isNative = false;
        this.meterInterval = null;
        this.audioContext = null;
        this.mediaStream = null;
        this.processor = null;
        this.micSocket = null;
        this.inputLevel = 0; // 0.0 to 1.0 for HUD meter
        this.onLevelChange = null;
        this.onStatusChange = null;

        this.targetSampleRate = 16000;
        this.chunkSize = 1024; // ~64ms per audio chunk at 16kHz
    }

    async toggleMic() {
        if (this.isStreaming) {
            this.stop();
        } else {
            await this.start();
        }
    }

    async start() {
        if (this.isStreaming) return;

        // 1. Check for Native Android bridge first (bypasses browser HTTP security & zero overhead)
        if (window.OmniPadNative && typeof window.OmniPadNative.startNativeMic === 'function') {
            try {
                const started = window.OmniPadNative.startNativeMic();
                if (started) {
                    this.isStreaming = true;
                    this.isNative = true;
                    this.isMuted = false;
                    if (this.onStatusChange) this.onStatusChange('live');

                    if (this.meterInterval) clearInterval(this.meterInterval);
                    this.meterInterval = setInterval(() => {
                        if (!this.isStreaming) {
                            clearInterval(this.meterInterval);
                            return;
                        }
                        if (window.OmniPadNative && typeof window.OmniPadNative.getNativeMicLevel === 'function') {
                            const level = window.OmniPadNative.getNativeMicLevel();
                            this.inputLevel = level;
                            if (this.onLevelChange) this.onLevelChange(level);
                        }
                        if (window.OmniPadNative && typeof window.OmniPadNative.isNativeMicStreaming === 'function') {
                            if (!window.OmniPadNative.isNativeMicStreaming()) {
                                this.stop();
                            }
                        }
                    }, 50);
                    return;
                } else {
                    console.warn('[MicEngine] Native mic start returned false (permission requested or busy)');
                }
            } catch (nativeErr) {
                console.warn('[MicEngine] Native mic bridge error:', nativeErr);
            }
        }

        // 2. Fallback to Web Audio API for browser / secure context
        try {
            if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
                throw new Error("Microphone capture not supported in this browser.");
            }

            // Request microphone access with echo cancellation & noise suppression
            this.mediaStream = await navigator.mediaDevices.getUserMedia({
                audio: {
                    channelCount: 1,
                    sampleRate: this.targetSampleRate,
                    echoCancellation: true,
                    noiseSuppression: true,
                    autoGainControl: true
                },
                video: false
            });

            // Setup AudioContext & ScriptProcessor (Compatible with Chrome 59 / Android 7.1.2)
            const AudioContextClass = window.AudioContext || window.webkitAudioContext;
            this.audioContext = new AudioContextClass({ sampleRate: this.targetSampleRate });

            const source = this.audioContext.createMediaStreamSource(this.mediaStream);
            this.processor = this.audioContext.createScriptProcessor(this.chunkSize, 1, 1);

            // Connect dedicated WebSocket for microphone audio
            const host = (this.network && this.network.currentHost) ? this.network.currentHost : location.host;
            const protocol = location.protocol === 'https:' ? 'wss:' : 'ws:';
            this.micSocket = new WebSocket(`${protocol}//${host}/ws/mic`);
            this.micSocket.binaryType = 'arraybuffer';

            this.micSocket.onopen = () => {
                this.isStreaming = true;
                this.isNative = false;
                if (this.onStatusChange) this.onStatusChange('live');
            };

            this.micSocket.onclose = () => {
                if (this.isStreaming) {
                    this.stop();
                }
            };

            this.micSocket.onerror = (e) => {
                console.warn('[MicEngine] WebSocket error:', e);
            };

            // Audio Processing Loop: Float32 -> 16-bit PCM Little Endian
            this.processor.onaudioprocess = (e) => {
                if (!this.isStreaming || this.isMuted) {
                    this.inputLevel = 0;
                    if (this.onLevelChange) this.onLevelChange(0);
                    return;
                }

                const inputBuffer = e.inputBuffer.getChannelData(0);
                const len = inputBuffer.length;
                const pcm16 = new Int16Array(len);

                let sumSquares = 0;
                for (let i = 0; i < len; i++) {
                    let sample = Math.max(-1.0, Math.min(1.0, inputBuffer[i]));
                    sumSquares += sample * sample;
                    pcm16[i] = sample < 0 ? sample * 0x8000 : sample * 0x7FFF;
                }

                // Calculate RMS level for UI meter
                const rms = Math.sqrt(sumSquares / len);
                this.inputLevel = Math.min(1.0, rms * 4.0); // Amplified for UI meter response
                if (this.onLevelChange) this.onLevelChange(this.inputLevel);

                // Send PCM binary chunk if socket is open
                if (this.micSocket && this.micSocket.readyState === WebSocket.OPEN) {
                    this.micSocket.send(pcm16.buffer);
                }
            };

            source.connect(this.processor);
            this.processor.connect(this.audioContext.destination);

        } catch (err) {
            console.error('[MicEngine] Failed to start microphone:', err);
            this.stop();
            if (this.onStatusChange) this.onStatusChange('error', err.message || 'Permission denied');
        }
    }

    toggleMute() {
        this.isMuted = !this.isMuted;
        if (this.isNative && window.OmniPadNative && typeof window.OmniPadNative.setNativeMicMuted === 'function') {
            window.OmniPadNative.setNativeMicMuted(this.isMuted);
        }
        if (this.onStatusChange) this.onStatusChange(this.isMuted ? 'muted' : 'live');
        return this.isMuted;
    }

    stop() {
        this.isStreaming = false;
        this.inputLevel = 0;

        if (this.meterInterval) {
            clearInterval(this.meterInterval);
            this.meterInterval = null;
        }

        if (this.isNative) {
            if (window.OmniPadNative && typeof window.OmniPadNative.stopNativeMic === 'function') {
                try { window.OmniPadNative.stopNativeMic(); } catch (_) {}
            }
            this.isNative = false;
        }

        if (this.processor) {
            try { this.processor.disconnect(); } catch (_) {}
            this.processor = null;
        }

        if (this.mediaStream) {
            try {
                this.mediaStream.getTracks().forEach(t => t.stop());
            } catch (_) {}
            this.mediaStream = null;
        }

        if (this.audioContext && this.audioContext.state !== 'closed') {
            try { this.audioContext.close(); } catch (_) {}
            this.audioContext = null;
        }

        if (this.micSocket) {
            try { this.micSocket.close(); } catch (_) {}
            this.micSocket = null;
        }

        if (this.onStatusChange) this.onStatusChange('off');
        if (this.onLevelChange) this.onLevelChange(0);
    }
}
