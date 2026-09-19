/**
 * OmniPad Audio Engine (Virtual Controller Headphone Jack)
 * Ultra-low latency game audio streaming directly to phone via Web Audio API.
 */
class AudioEngine {
    constructor() {
        this.socket = null;
        this.audioCtx = null;
        this.gainNode = null;
        this.isStreaming = false;
        this.sampleRate = 48000;
        this.channels = 2;
        this.nextPlayTime = 0;
        this.volume = 0.85;
        this.onStateChange = null;
    }

    async toggle() {
        if (this.isStreaming) {
            this.stop();
        } else {
            await this.start();
        }
    }

    async start() {
        if (this.isStreaming) return;

        try {
            // AudioContext must be unlocked by a user gesture
            const AudioContextClass = window.AudioContext || window.webkitAudioContext;
            if (!this.audioCtx || this.audioCtx.state === 'closed') {
                this.audioCtx = new AudioContextClass({ sampleRate: this.sampleRate });
            }
            if (this.audioCtx.state === 'suspended') {
                await this.audioCtx.resume();
            }

            // Gain / Volume Node
            this.gainNode = this.audioCtx.createGain();
            this.gainNode.gain.value = this.volume;
            this.gainNode.connect(this.audioCtx.destination);

            const protocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
            const audioUrl = `${protocol}//${window.location.host}/audio`;

            this.socket = new WebSocket(audioUrl);
            this.socket.binaryType = 'arraybuffer';

            this.socket.onopen = () => {
                this.isStreaming = true;
                this.nextPlayTime = 0;
                if (this.onStateChange) this.onStateChange(true);
            };

            this.socket.onmessage = (event) => {
                if (typeof event.data === 'string') {
                    try {
                        const meta = JSON.parse(event.data);
                        if (meta.type === 'audio_init') {
                            this.sampleRate = meta.sampleRate || 48000;
                            this.channels = meta.channels || 2;
                            if (this.audioCtx && this.audioCtx.sampleRate !== this.sampleRate) {
                                try {
                                    this.audioCtx.close();
                                    const AudioContextClass = window.AudioContext || window.webkitAudioContext;
                                    this.audioCtx = new AudioContextClass({ sampleRate: this.sampleRate });
                                    this.gainNode = this.audioCtx.createGain();
                                    this.gainNode.gain.value = this.volume;
                                    this.gainNode.connect(this.audioCtx.destination);
                                } catch { }
                            }
                        }
                    } catch { }
                    return;
                }

                if (event.data instanceof ArrayBuffer) {
                    this.playPcmChunk(event.data);
                }
            };

            this.socket.onclose = () => {
                this.stop();
            };

            this.socket.onerror = () => {
                this.stop();
            };
        } catch (err) {
            console.warn('[Audio Engine] Could not start audio stream:', err);
            this.stop();
        }
    }

    playPcmChunk(arrayBuffer) {
        if (!this.audioCtx || !this.gainNode || this.audioCtx.state !== 'running') return;

        const int16Array = new Int16Array(arrayBuffer);
        const frameCount = Math.floor(int16Array.length / this.channels);
        if (frameCount <= 0) return;

        // Create buffer matching the audio hardware sample rate
        const audioBuffer = this.audioCtx.createBuffer(this.channels, frameCount, this.sampleRate);

        if (this.channels === 2) {
            const leftChannel = audioBuffer.getChannelData(0);
            const rightChannel = audioBuffer.getChannelData(1);

            for (let i = 0; i < frameCount; i++) {
                leftChannel[i] = int16Array[i * 2] / 32768.0;
                rightChannel[i] = int16Array[i * 2 + 1] / 32768.0;
            }
        } else {
            const channel = audioBuffer.getChannelData(0);
            for (let i = 0; i < frameCount; i++) {
                channel[i] = int16Array[i] / 32768.0;
            }
        }

        const source = this.audioCtx.createBufferSource();
        source.buffer = audioBuffer;
        source.connect(this.gainNode);

        const now = this.audioCtx.currentTime;
        const TARGET_LEAD = 0.045; // 45ms target jitter lead (glitch-free, imperceptible latency)
        const MAX_LEAD = 0.120;    // 120ms max lead before drift correction

        if (this.nextPlayTime === 0) {
            // Initial stream start: pre-buffer 45ms
            this.nextPlayTime = now + TARGET_LEAD;
        } else if (this.nextPlayTime <= now) {
            // Underrun: schedule immediately to eliminate silence gap
            this.nextPlayTime = now + 0.005;
        } else if (this.nextPlayTime > now + MAX_LEAD) {
            // Realign if drift accumulates
            this.nextPlayTime = now + TARGET_LEAD;
        }

        source.start(this.nextPlayTime);
        this.nextPlayTime += audioBuffer.duration;
    }

    stop() {
        if (this.socket) {
            try { this.socket.close(); } catch { }
            this.socket = null;
        }
        this.isStreaming = false;
        this.nextPlayTime = 0;
        if (this.onStateChange) this.onStateChange(false);
    }

    setVolume(val) {
        this.volume = Math.max(0, Math.min(1, val));
        if (this.gainNode && this.audioCtx) {
            this.gainNode.gain.setValueAtTime(this.volume, this.audioCtx.currentTime);
        }
    }
}
