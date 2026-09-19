package com.omnipad.client.ui

import android.app.Activity
import android.app.AlertDialog
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.content.SharedPreferences
import android.graphics.Color
import android.net.wifi.WifiManager
import android.os.BatteryManager
import android.os.Build
import android.os.Bundle
import android.os.VibrationEffect
import android.os.Vibrator
import android.os.VibratorManager
import android.text.InputType
import android.view.KeyEvent
import android.view.View
import android.view.WindowInsets
import android.view.WindowInsetsController
import android.view.WindowManager
import android.webkit.JavascriptInterface
import android.webkit.WebChromeClient
import android.webkit.WebResourceError
import android.webkit.WebResourceRequest
import android.webkit.WebSettings
import android.webkit.WebView
import android.webkit.WebViewClient
import android.widget.EditText
import android.widget.LinearLayout
import android.widget.TextView
import android.widget.Toast
import com.omnipad.client.protocol.Protocol
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.NetworkInterface
import java.net.Socket
import java.util.concurrent.Executors

class MainActivity : Activity() {

    private lateinit var webView: WebView
    private var vibrator: Vibrator? = null
    private lateinit var prefs: SharedPreferences
    private val backgroundExecutor = Executors.newCachedThreadPool()
    private var lastBackPressTime = 0L
    private var isConnectedToServer = false

    companion object {
        private const val PREFS_NAME = "omnipad_prefs"
        private const val KEY_LAST_IP = "last_server_ip"
        private const val SERVER_PORT = 27502
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        prefs = getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)

        // 1. Keep Screen Awake & True Edge-to-Edge Immersive
        window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.P) {
            window.attributes.layoutInDisplayCutoutMode =
                WindowManager.LayoutParams.LAYOUT_IN_DISPLAY_CUTOUT_MODE_SHORT_EDGES
        }
        // 2. Vibrator Setup (Handles Android 5.0 through Android 16+)
        vibrator = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
            val manager = getSystemService(Context.VIBRATOR_MANAGER_SERVICE) as? VibratorManager
            manager?.defaultVibrator ?: (getSystemService(Context.VIBRATOR_SERVICE) as Vibrator)
        } else {
            @Suppress("DEPRECATION")
            getSystemService(Context.VIBRATOR_SERVICE) as Vibrator
        }

        // 3. Hardware-Accelerated WebView
        webView = WebView(this).apply {
            setBackgroundColor(Color.parseColor("#0a0d14")) // Match dark gamepad theme
        }
        setContentView(webView)

        window.decorView.post { hideSystemBars() }

        configureWebView()

        // 4. Start Server Connection / Discovery
        autoConnectOrDiscover()

        // 5. Monitor USB Cable Connection Lifecycle for Seamless Auto-Switching
        val filter = IntentFilter().apply {
            addAction(Intent.ACTION_POWER_CONNECTED)
            addAction(Intent.ACTION_POWER_DISCONNECTED)
        }
        try {
            registerReceiver(powerReceiver, filter)
        } catch (_: Exception) { }
    }

    private val powerReceiver = object : BroadcastReceiver() {
        override fun onReceive(context: Context?, intent: Intent?) {
            when (intent?.action) {
                Intent.ACTION_POWER_CONNECTED -> {
                    webView.post {
                        webView.evaluateJavascript("if (window.omnipadApp && window.omnipadApp.network) window.omnipadApp.network.onUsbConnected();", null)
                    }
                }
                Intent.ACTION_POWER_DISCONNECTED -> {
                    webView.post {
                        webView.evaluateJavascript("if (window.omnipadApp && window.omnipadApp.network) window.omnipadApp.network.onUsbDisconnected();", null)
                    }
                }
            }
        }
    }

    override fun onWindowFocusChanged(hasFocus: Boolean) {
        super.onWindowFocusChanged(hasFocus)
        if (hasFocus) {
            hideSystemBars()
        }
    }

    private fun hideSystemBars() {
        try {
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
                window.setDecorFitsSystemWindows(false)
                window.insetsController?.let { controller ->
                    controller.hide(WindowInsets.Type.systemBars())
                    controller.systemBarsBehavior =
                        WindowInsetsController.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE
                }
            } else {
                @Suppress("DEPRECATION")
                window.decorView.systemUiVisibility = (
                    View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY
                    or View.SYSTEM_UI_FLAG_FULLSCREEN
                    or View.SYSTEM_UI_FLAG_HIDE_NAVIGATION
                    or View.SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN
                    or View.SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION
                    or View.SYSTEM_UI_FLAG_LAYOUT_STABLE
                )
            }
        } catch (_: Exception) { }
    }

    private fun configureWebView() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.KITKAT) {
            WebView.setWebContentsDebuggingEnabled(true)
        }
        webView.settings.apply {
            javaScriptEnabled = true
            domStorageEnabled = true
            @Suppress("DEPRECATION")
            databaseEnabled = true
            allowFileAccess = true
            allowContentAccess = true
            @Suppress("DEPRECATION")
            allowFileAccessFromFileURLs = true
            @Suppress("DEPRECATION")
            allowUniversalAccessFromFileURLs = true
            useWideViewPort = true
            loadWithOverviewMode = true
            cacheMode = WebSettings.LOAD_NO_CACHE
            mediaPlaybackRequiresUserGesture = false
        }

        webView.addJavascriptInterface(OmniPadBridge(), "OmniPadNative")

        webView.webViewClient = object : WebViewClient() {
            override fun onPageFinished(view: WebView?, url: String?) {
                super.onPageFinished(view, url)
                isConnectedToServer = true
            }

            override fun onReceivedError(
                view: WebView?,
                request: WebResourceRequest?,
                error: WebResourceError?
            ) {
                super.onReceivedError(view, request, error)
                if (request?.isForMainFrame == true) {
                    isConnectedToServer = false
                }
            }
        }

        webView.webChromeClient = WebChromeClient()
    }

    // Auto-Discovery: Loopback USB -> UDP Broadcast -> Saved IP -> Manual Prompt
    private fun autoConnectOrDiscover() {
        backgroundExecutor.execute {
            // A. Check USB ADB Reverse Loopback (127.0.0.1:27502)
            if (isPortReachable("127.0.0.1", SERVER_PORT, 200)) {
                runOnUiThread {
                    loadServer("127.0.0.1")
                }
                return@execute
            }

            // B. UDP Auto-Discovery Broadcast on port 27501
            val discoveredIp = performUdpDiscovery(2000)
            if (discoveredIp != null) {
                runOnUiThread {
                    saveServerIp(discoveredIp)
                    loadServer(discoveredIp)
                    Toast.makeText(this, "OmniPad Server Found: $discoveredIp", Toast.LENGTH_SHORT).show()
                }
                return@execute
            }

            // C. Check Last Known Server IP
            val lastIp = prefs.getString(KEY_LAST_IP, null)
            if (!lastIp.isNullOrBlank() && isPortReachable(lastIp, SERVER_PORT, 500)) {
                runOnUiThread {
                    loadServer(lastIp)
                }
                return@execute
            }

            // D. Fallback: Prompt user with Dialog
            runOnUiThread {
                showServerAddressDialog(lastIp ?: "192.168.")
            }
        }
    }

    private fun isPortReachable(host: String, port: Int, timeoutMs: Int): Boolean {
        return try {
            Socket().use { socket ->
                socket.connect(InetSocketAddress(host, port), timeoutMs)
                true
            }
        } catch (_: Exception) {
            false
        }
    }

    private fun performUdpDiscovery(timeoutMs: Int): String? {
        var multicastLock: WifiManager.MulticastLock? = null
        try {
            val wifi = applicationContext.getSystemService(Context.WIFI_SERVICE) as? WifiManager
            multicastLock = wifi?.createMulticastLock("omnipad_discovery")?.apply {
                setReferenceCounted(true)
                acquire()
            }

            val socket = DatagramSocket().apply {
                soTimeout = timeoutMs
                broadcast = true
            }

            val discoveryPayload = byteArrayOf(
                Protocol.MAGIC_BYTE,
                Protocol.VERSION,
                Protocol.MSG_DISCOVER,
                Protocol.NO_PAD
            )

            // Send to 255.255.255.255
            val globalPacket = DatagramPacket(
                discoveryPayload,
                discoveryPayload.size,
                InetAddress.getByName("255.255.255.255"),
                Protocol.DISCOVERY_PORT
            )
            socket.send(globalPacket)

            // Also broadcast on all active non-loopback network interfaces
            val interfaces = NetworkInterface.getNetworkInterfaces()
            while (interfaces.hasMoreElements()) {
                val netIf = interfaces.nextElement()
                if (netIf.isLoopback || !netIf.isUp) continue
                for (addr in netIf.interfaceAddresses) {
                    val broadcast = addr.broadcast ?: continue
                    val p = DatagramPacket(discoveryPayload, discoveryPayload.size, broadcast, Protocol.DISCOVERY_PORT)
                    try { socket.send(p) } catch (_: Exception) { }
                }
            }

            val buffer = ByteArray(32)
            val receivePacket = DatagramPacket(buffer, buffer.size)

            val startTime = System.currentTimeMillis()
            while (System.currentTimeMillis() - startTime < timeoutMs) {
                try {
                    socket.receive(receivePacket)
                    if (receivePacket.length >= 4 &&
                        buffer[0] == Protocol.MAGIC_BYTE &&
                        buffer[1] == Protocol.VERSION &&
                        buffer[2] == Protocol.MSG_WELCOME
                    ) {
                        val serverIp = receivePacket.address.hostAddress
                        socket.close()
                        return serverIp
                    }
                } catch (_: Exception) {
                    break
                }
            }
            socket.close()
        } catch (_: Exception) {
        } finally {
            try { multicastLock?.release() } catch (_: Exception) { }
        }
        return null
    }

    private fun loadServer(ip: String) {
        saveServerIp(ip)
        val targetUrl = "http://$ip:$SERVER_PORT/"
        webView.loadUrl(targetUrl)
    }

    private fun loadOffline() {
        webView.loadUrl("file:///android_asset/web/index.html")
        Toast.makeText(this, "Running in Offline / Customizer Mode", Toast.LENGTH_SHORT).show()
    }

    private fun saveServerIp(ip: String) {
        prefs.edit().putString(KEY_LAST_IP, ip).apply()
    }

    private fun showServerAddressDialog(prefillIp: String) {
        val layout = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(50, 40, 50, 10)
        }

        val promptText = TextView(this).apply {
            text = "Could not find PC server automatically.\nEnter the IP address of your Windows PC:"
            textSize = 14f
            setTextColor(Color.LTGRAY)
        }
        layout.addView(promptText)

        val input = EditText(this).apply {
            inputType = InputType.TYPE_CLASS_PHONE
            setText(prefillIp)
            setSelection(text.length)
        }
        layout.addView(input)

        AlertDialog.Builder(this)
            .setTitle("OmniPad Connection")
            .setView(layout)
            .setPositiveButton("Connect") { _, _ ->
                val ip = input.text.toString().trim()
                if (ip.isNotEmpty()) {
                    loadServer(ip)
                }
            }
            .setNeutralButton("Rescan") { _, _ ->
                Toast.makeText(this, "Scanning for PC...", Toast.LENGTH_SHORT).show()
                autoConnectOrDiscover()
            }
            .setNegativeButton("Offline Mode") { _, _ ->
                loadOffline()
            }
            .setCancelable(false)
            .show()
    }

    // Hardware Volume Buttons as Tactile Bumpers (LB / RB)
    override fun onKeyDown(keyCode: Int, event: KeyEvent?): Boolean {
        if (keyCode == KeyEvent.KEYCODE_VOLUME_UP) {
            triggerHaptic(20)
            webView.evaluateJavascript("if (window.omniPadTriggerButton) window.omniPadTriggerButton('LB', true);", null)
            return true // Suppress Android system volume slider
        }
        if (keyCode == KeyEvent.KEYCODE_VOLUME_DOWN) {
            triggerHaptic(20)
            webView.evaluateJavascript("if (window.omniPadTriggerButton) window.omniPadTriggerButton('RB', true);", null)
            return true // Suppress Android system volume slider
        }
        return super.onKeyDown(keyCode, event)
    }

    override fun onKeyUp(keyCode: Int, event: KeyEvent?): Boolean {
        if (keyCode == KeyEvent.KEYCODE_VOLUME_UP) {
            webView.evaluateJavascript("if (window.omniPadTriggerButton) window.omniPadTriggerButton('LB', false);", null)
            return true
        }
        if (keyCode == KeyEvent.KEYCODE_VOLUME_DOWN) {
            webView.evaluateJavascript("if (window.omniPadTriggerButton) window.omniPadTriggerButton('RB', false);", null)
            return true
        }
        return super.onKeyUp(keyCode, event)
    }

    private fun triggerHaptic(durationMs: Long, amplitude: Int = VibrationEffect.DEFAULT_AMPLITUDE) {
        backgroundExecutor.execute {
            try {
                if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
                    val attrs = android.os.VibrationAttributes.Builder()
                        .setUsage(android.os.VibrationAttributes.USAGE_TOUCH)
                        .build()
                    vibrator?.vibrate(VibrationEffect.createOneShot(durationMs, amplitude), attrs)
                } else if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                    val audioAttrs = android.media.AudioAttributes.Builder()
                        .setContentType(android.media.AudioAttributes.CONTENT_TYPE_SONIFICATION)
                        .setUsage(android.media.AudioAttributes.USAGE_ASSISTANCE_SONIFICATION)
                        .setFlags(android.media.AudioAttributes.FLAG_AUDIBILITY_ENFORCED)
                        .build()
                    vibrator?.vibrate(VibrationEffect.createOneShot(durationMs, amplitude), audioAttrs)
                } else {
                    @Suppress("DEPRECATION")
                    vibrator?.vibrate(durationMs)
                }
            } catch (_: Exception) { }
        }
    }

    // Double-tap back button to prevent accidental exits during gameplay
    @Suppress("DEPRECATION")
    override fun onBackPressed() {
        val now = System.currentTimeMillis()
        if (now - lastBackPressTime < 2000) {
            super.onBackPressed()
        } else {
            lastBackPressTime = now
            Toast.makeText(this, "Press BACK again to exit OmniPad", Toast.LENGTH_SHORT).show()
        }
    }

    override fun onDestroy() {
        super.onDestroy()
        try { unregisterReceiver(powerReceiver) } catch (_: Exception) { }
        backgroundExecutor.shutdownNow()
        webView.destroy()
    }

    // Native JavaScript Interface Bridge
    inner class OmniPadBridge {
        @JavascriptInterface
        fun vibrate(durationMs: Long) {
            triggerHaptic(durationMs)
        }

        @JavascriptInterface
        fun vibrateHeavy(durationMs: Long) {
            triggerHaptic(durationMs, 255)
        }

        @JavascriptInterface
        fun getBatteryLevel(): Int {
            return try {
                val bm = getSystemService(Context.BATTERY_SERVICE) as? BatteryManager
                bm?.getIntProperty(BatteryManager.BATTERY_PROPERTY_CAPACITY) ?: -1
            } catch (_: Exception) {
                -1
            }
        }

        @JavascriptInterface
        fun showToast(message: String) {
            runOnUiThread {
                Toast.makeText(this@MainActivity, message, Toast.LENGTH_SHORT).show()
            }
        }

        @JavascriptInterface
        fun openSettings() {
            runOnUiThread {
                val lastIp = prefs.getString(KEY_LAST_IP, "") ?: ""
                showServerAddressDialog(lastIp)
            }
        }
    }
}
