/**
 * OmniPad Low-Latency Screen Streaming Engine
 * Receives desktop/game screen frames from PCServer (/ws/screen)
 * and renders them onto a background canvas behind the controller layout.
 * Strictly decoupled from controller input loops with adaptive frame dropping to guarantee zero touch latency.
 */
class ScreenStreamEngine {
    constructor(networkClient) {
        this.network = networkClient;
        this.isStreaming = false;
        this.socket = null;
        this.canvas = null;
        this.ctx = null;

        // Controller component opacity (0.10 to 1.00)
        this.controlOpacity = 0.85;

        // Performance & frame dropping state
        this.isRendering = false;
        this.currentImage = new Image();
        this.pendingBlobUrl = null;
        this.fpsCounter = 0;
        this.lastFpsCheck = performance.now();
        this.currentFps = 0;

        this.onStatusChange = null;
        this.onFpsUpdate = null;

        this.initCanvas();
    }

    initCanvas() {
        this.canvas = document.getElementById('game-stream-canvas');
        if (this.canvas) {
            this.ctx = this.canvas.getContext('2d', { alpha: false });
            this.resizeCanvas();
            window.addEventListener('resize', () => this.resizeCanvas());
            window.addEventListener('orientationchange', () => setTimeout(() => this.resizeCanvas(), 300));
        }

        // Load saved opacity preference
        try {
            const savedOpacity = localStorage.getItem('omnipad_control_opacity');
            if (savedOpacity) {
                this.setControlOpacity(parseFloat(savedOpacity));
            } else {
                this.setControlOpacity(0.85);
            }
        } catch (_) {
            this.setControlOpacity(0.85);
        }
    }

    resizeCanvas() {
        if (!this.canvas) return;
        this.canvas.width = window.innerWidth;
        this.canvas.height = window.innerHeight;
    }

    setControlOpacity(val) {
        const clamped = Math.max(0.10, Math.min(1.0, val));
        this.controlOpacity = clamped;
        document.documentElement.style.setProperty('--control-opacity', clamped.toString());
        try { localStorage.setItem('omnipad_control_opacity', clamped.toString()); } catch (_) {}
    }

    toggleStream() {
        if (this.isStreaming) {
            this.stop();
        } else {
            this.start();
        }
    }

    start() {
        if (this.isStreaming) return;
        if (!this.canvas) this.initCanvas();

        const host = (this.network && this.network.currentHost) ? this.network.currentHost : location.host;
        const protocol = location.protocol === 'https:' ? 'wss:' : 'ws:';
        const streamUrl = `${protocol}//${host}/ws/screen`;

        try {
            this.socket = new WebSocket(streamUrl);
            this.socket.binaryType = 'blob';

            this.socket.onopen = () => {
                this.isStreaming = true;
                if (this.canvas) this.canvas.classList.remove('hidden');
                if (this.onStatusChange) this.onStatusChange('active');
            };

            this.socket.onmessage = (event) => {
                // Adaptive Frame Dropping: If previous frame is still rendering, drop this frame!
                if (this.isRendering) {
                    return;
                }

                if (event.data instanceof Blob) {
                    this.isRendering = true;

                    // Clean up previous blob URL to prevent memory leaks
                    if (this.pendingBlobUrl) {
                        URL.revokeObjectURL(this.pendingBlobUrl);
                    }

                    this.pendingBlobUrl = URL.createObjectURL(event.data);
                    const renderTimeout = setTimeout(() => {
                        if (this.isRendering) {
                            this.isRendering = false;
                            if (this.pendingBlobUrl) {
                                URL.revokeObjectURL(this.pendingBlobUrl);
                                this.pendingBlobUrl = null;
                            }
                        }
                    }, 60);

                    this.currentImage.onload = () => {
                        clearTimeout(renderTimeout);
                        if (this.pendingBlobUrl) {
                            URL.revokeObjectURL(this.pendingBlobUrl);
                            this.pendingBlobUrl = null;
                        }
                        requestAnimationFrame(() => {
                            if (this.ctx && this.canvas) {
                                this.ctx.drawImage(this.currentImage, 0, 0, this.canvas.width, this.canvas.height);
                            }
                            this.isRendering = false;

                            // Calculate FPS
                            this.fpsCounter++;
                            const now = performance.now();
                            if (now - this.lastFpsCheck >= 1000) {
                                this.currentFps = Math.round((this.fpsCounter * 1000) / (now - this.lastFpsCheck));
                                this.fpsCounter = 0;
                                this.lastFpsCheck = now;
                                if (this.onFpsUpdate) this.onFpsUpdate(this.currentFps);
                            }
                        });
                    };

                    this.currentImage.onerror = () => {
                        clearTimeout(renderTimeout);
                        if (this.pendingBlobUrl) {
                            URL.revokeObjectURL(this.pendingBlobUrl);
                            this.pendingBlobUrl = null;
                        }
                        this.isRendering = false;
                    };

                    this.currentImage.src = this.pendingBlobUrl;
                }
            };

            this.socket.onclose = () => {
                if (this.isStreaming) {
                    this.stop();
                }
            };

            this.socket.onerror = (err) => {
                console.warn('[ScreenStream] Socket error:', err);
                this.stop();
            };

        } catch (err) {
            console.error('[ScreenStream] Failed to start screen stream:', err);
            this.stop();
        }
    }

    stop() {
        this.isStreaming = false;
        this.isRendering = false;

        if (this.socket) {
            try { this.socket.close(); } catch (_) {}
            this.socket = null;
        }

        if (this.pendingBlobUrl) {
            URL.revokeObjectURL(this.pendingBlobUrl);
            this.pendingBlobUrl = null;
        }

        if (this.canvas) {
            this.canvas.classList.add('hidden');
            if (this.ctx) {
                this.ctx.clearRect(0, 0, this.canvas.width, this.canvas.height);
            }
        }

        if (this.onStatusChange) this.onStatusChange('off');
        if (this.onFpsUpdate) this.onFpsUpdate(0);
    }
}
