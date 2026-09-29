package com.omnipad.client.ui

import android.app.AlertDialog
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.content.SharedPreferences
import android.graphics.Color
import android.media.AudioFormat
import android.media.AudioRecord
import android.media.MediaRecorder
import android.net.Uri
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
import android.webkit.PermissionRequest
import android.webkit.WebChromeClient
import android.webkit.WebResourceError
import android.webkit.WebResourceRequest
import android.webkit.WebSettings
import android.webkit.WebView
import android.webkit.WebViewClient
import android.widget.Button
import android.widget.EditText
import android.widget.LinearLayout
import android.widget.TextView
import android.widget.Toast
import android.bluetooth.BluetoothManager
import android.content.pm.PackageManager
import androidx.activity.ComponentActivity
import androidx.core.content.ContextCompat
import com.journeyapps.barcodescanner.ScanContract
import com.journeyapps.barcodescanner.ScanOptions
import com.omnipad.client.protocol.DiscoveredServer
import com.omnipad.client.protocol.Protocol
import com.omnipad.client.transport.BluetoothHidManager
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.NetworkInterface
import java.net.Socket
import java.net.SocketTimeoutException
import java.util.concurrent.Executors

class MainActivity : ComponentActivity() {

    private lateinit var webView: WebView
    private var vibrator: Vibrator? = null
    private lateinit var prefs: SharedPreferences
    private val backgroundExecutor = Executors.newCachedThreadPool()
    private var lastBackPressTime = 0L
    private var isConnectedToServer = false

    private val qrScannerLauncher = registerForActivityResult(ScanContract()) { result ->
        if (result.contents != null) {
            handleScannedQr(result.contents)
        }
    }

    private var bluetoothHidManager: BluetoothHidManager? = null
    private var bluetoothStatus = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.P) "standby" else "unsupported"

    private var nativeAudioRecord: AudioRecord? = null
    @Volatile private var isNativeRecording = false
    @Volatile private var isNativeMicMuted = false
    @Volatile private var nativeMicLevel = 0f
    private var nativeMicThread: Thread? = null
    private var currentConnectedServerIp: String = "127.0.0.1"
    private var currentConnectedServerName: String = ""
    private var currentConnectedServerPort: Int = SERVER_PORT

    companion object {
        private const val PREFS_NAME = "omnipad_prefs"
        private const val KEY_LAST_IP = "last_server_ip"
        private const val SERVER_PORT = 27502
        private const val RC_BLUETOOTH_PERMISSIONS = 1001
        private const val RC_MIC_PERMISSION = 1002
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

        // 4. Initialize Bluetooth HID Gamepad Manager (Android 9+)
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.P) {
            bluetoothHidManager = BluetoothHidManager(this).apply {
                onStatusChanged = { status ->
                    bluetoothStatus = status
                    val hostName = getConnectedHostName() ?: ""
                    webView.post {
                        webView.evaluateJavascript(
                            "if (window.omnipadApp && window.omnipadApp.network) window.omnipadApp.network.onBluetoothStatusChanged('$status', '$hostName');",
                            null
                        )
                    }
                }
            }
        }

        // 5. Check Bluetooth Permissions on Android 12+
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
            checkAndRequestBluetoothPermissions()
        }

        // 5b. Check Audio Record Permission on Android 6+ (M+)
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
            if (checkSelfPermission(android.Manifest.permission.RECORD_AUDIO) != PackageManager.PERMISSION_GRANTED) {
                requestPermissions(arrayOf(android.Manifest.permission.RECORD_AUDIO), 1002)
            }
        }

        // 6. Handle Deep Link or Auto-Connect / Auto-Discover
        val handledDeepLink = handleIncomingIntent(intent)
        if (!handledDeepLink) {
            autoConnectOrDiscover()
        }

        // 7. Monitor USB Cable Connection Lifecycle for Seamless Auto-Switching
        val filter = IntentFilter().apply {
            addAction(Intent.ACTION_POWER_CONNECTED)
            addAction(Intent.ACTION_POWER_DISCONNECTED)
        }
        try {
            ContextCompat.registerReceiver(this, powerReceiver, filter, ContextCompat.RECEIVER_EXPORTED)
        } catch (_: Exception) { }
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        setIntent(intent)
        handleIncomingIntent(intent)
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
                    view?.postDelayed({
                        if (!isConnectedToServer && !isFinishing) {
                            autoConnectOrDiscover()
                        }
                    }, 2500)
                }
            }
        }

        webView.webChromeClient = object : WebChromeClient() {
            override fun onPermissionRequest(request: PermissionRequest?) {
                if (request == null) return
                val requestedResources = request.resources
                val granted = ArrayList<String>()
                for (resource in requestedResources) {
                    if (resource == PermissionRequest.RESOURCE_AUDIO_CAPTURE) {
                        granted.add(resource)
                    }
                }
                if (granted.isNotEmpty()) {
                    request.grant(granted.toTypedArray())
                } else {
                    request.deny()
                }
            }
        }
    }

    private fun isUsbCableConnected(): Boolean {
        return try {
            val batteryIntent = registerReceiver(null, IntentFilter(Intent.ACTION_BATTERY_CHANGED))
            val plugged = batteryIntent?.getIntExtra(BatteryManager.EXTRA_PLUGGED, -1) ?: -1
            plugged == BatteryManager.BATTERY_PLUGGED_USB
        } catch (_: Exception) {
            false
        }
    }

    // Auto-Discovery: Real USB Cable -> UDP WiFi Broadcast -> Saved LAN IP -> Fallback Loopback -> Prompt
    private fun autoConnectOrDiscover(forceShowPicker: Boolean = false) {
        backgroundExecutor.execute {
            // A. If physical USB cable is actually plugged in, prioritize Ultra-Low Latency USB Loopback (127.0.0.1:27502)
            if (!forceShowPicker && isUsbCableConnected() && isPortReachable("127.0.0.1", SERVER_PORT, 200)) {
                runOnUiThread {
                    loadServer("127.0.0.1", SERVER_PORT, "USB Loopback")
                }
                return@execute
            }

            // B. UDP Auto-Discovery Broadcast on port 27501 (True WiFi LAN)
            val discoveredServers = performUdpDiscovery(if (forceShowPicker) 1200 else 800)
            if (discoveredServers.isNotEmpty()) {
                if (discoveredServers.size > 1 || forceShowPicker) {
                    runOnUiThread {
                        showServerSelectionDialog(discoveredServers)
                    }
                    return@execute
                } else {
                    val server = discoveredServers[0]
                    runOnUiThread {
                        loadServer(server.ip, server.port, server.name)
                        Toast.makeText(this, "OmniPad Server Found: ${server.displayName}", Toast.LENGTH_SHORT).show()
                    }
                    return@execute
                }
            }

            if (forceShowPicker) {
                runOnUiThread {
                    showServerSelectionDialog(emptyList())
                }
                return@execute
            }

            // C. Check Last Known WiFi Server IP
            val lastIp = prefs.getString(KEY_LAST_IP, null)
            if (!lastIp.isNullOrBlank() && lastIp != "127.0.0.1" && lastIp != "localhost" && isPortReachable(lastIp, SERVER_PORT, 500)) {
                runOnUiThread {
                    loadServer(lastIp, SERVER_PORT)
                }
                return@execute
            }

            // D. Fallback: Loopback reachable (e.g. Wireless ADB proxy or local port forward)
            if (isPortReachable("127.0.0.1", SERVER_PORT, 200)) {
                runOnUiThread {
                    loadServer("127.0.0.1", SERVER_PORT)
                }
                return@execute
            }

            // E. Fallback: Prompt user with Dialog
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

    private fun performUdpDiscovery(timeoutMs: Int): List<DiscoveredServer> {
        val servers = mutableListOf<DiscoveredServer>()
        val seenIps = mutableSetOf<String>()
        var multicastLock: WifiManager.MulticastLock? = null
        var socket: DatagramSocket? = null
        try {
            val wifi = applicationContext.getSystemService(Context.WIFI_SERVICE) as? WifiManager
            multicastLock = wifi?.createMulticastLock("omnipad_discovery")?.apply {
                setReferenceCounted(true)
                acquire()
            }

            socket = DatagramSocket().apply {
                soTimeout = 200
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
            try { socket.send(globalPacket) } catch (_: Exception) {}

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

            val buffer = ByteArray(256)
            val startTime = System.currentTimeMillis()
            while (System.currentTimeMillis() - startTime < timeoutMs) {
                try {
                    val receivePacket = DatagramPacket(buffer, buffer.size)
                    socket.receive(receivePacket)
                    val len = receivePacket.length
                    if (len >= 4 &&
                        buffer[0] == Protocol.MAGIC_BYTE &&
                        buffer[1] == Protocol.VERSION &&
                        buffer[2] == Protocol.MSG_WELCOME
                    ) {
                        val serverIp = receivePacket.address.hostAddress ?: continue
                        var webPort = Protocol.DEFAULT_WEB_PORT
                        var machineName = ""
                        if (len >= 7) {
                            val portLow = buffer[4].toInt() and 0xFF
                            val portHigh = buffer[5].toInt() and 0xFF
                            val parsedPort = (portHigh shl 8) or portLow
                            if (parsedPort in 1..65535) {
                                webPort = parsedPort
                            }
                            val nameLen = buffer[6].toInt() and 0xFF
                            if (len >= 7 + nameLen && nameLen > 0) {
                                machineName = String(buffer, 7, nameLen, Charsets.UTF_8).trim()
                            }
                        }
                        val key = "$serverIp:$webPort:$machineName"
                        if (seenIps.add(key)) {
                            servers.add(DiscoveredServer(serverIp, machineName, webPort))
                        }
                    }
                } catch (_: SocketTimeoutException) {
                    // Single chunk timed out, continue until overall timeoutMs
                } catch (_: Exception) {
                    break
                }
            }
        } catch (_: Exception) {
        } finally {
            try { socket?.close() } catch (_: Exception) {}
            try { multicastLock?.release() } catch (_: Exception) {}
        }
        return servers
    }

    private fun showServerSelectionDialog(servers: List<DiscoveredServer>) {
        if (isFinishing || isDestroyed) return

        if (servers.isEmpty()) {
            val emptyItems = arrayOf(
                "📷 Scan Server QR Code",
                "🔄 Scan Wi-Fi Again",
                "➕ Enter IP Manually",
                "📁 Offline / Customizer Mode"
            )
            AlertDialog.Builder(this)
                .setTitle("OmniPad Servers (None Found)")
                .setItems(emptyItems) { _, which ->
                    when (which) {
                        0 -> launchQrScanner()
                        1 -> {
                            Toast.makeText(this, "Scanning for PC servers...", Toast.LENGTH_SHORT).show()
                            autoConnectOrDiscover(forceShowPicker = true)
                        }
                        2 -> {
                            val lastIp = prefs.getString(KEY_LAST_IP, "") ?: "192.168."
                            showServerAddressDialog(lastIp)
                        }
                        3 -> loadOffline()
                    }
                }
                .setNegativeButton("Cancel", null)
                .show()
            return
        }

        val items = ArrayList<String>()
        items.add("📷 Scan Server QR Code")
        for (server in servers) {
            val isCurrent = (server.ip == currentConnectedServerIp)
            val currentBadge = if (isCurrent) " [Connected]" else ""
            items.add("🖥️ ${server.displayName}$currentBadge")
        }
        items.add("➕ Enter Custom IP...")
        items.add("🔄 Rescan Wi-Fi Network")

        AlertDialog.Builder(this)
            .setTitle("Select OmniPad PC Server (${servers.size} Found)")
            .setItems(items.toTypedArray()) { _, which ->
                if (which == 0) {
                    launchQrScanner()
                } else if (which - 1 < servers.size) {
                    val chosen = servers[which - 1]
                    loadServer(chosen.ip, chosen.port, chosen.name)
                    Toast.makeText(this, "Connecting to ${chosen.displayName}...", Toast.LENGTH_SHORT).show()
                } else if (which - 1 == servers.size) {
                    val lastIp = prefs.getString(KEY_LAST_IP, "") ?: "192.168."
                    showServerAddressDialog(lastIp)
                } else {
                    Toast.makeText(this, "Scanning for PC servers...", Toast.LENGTH_SHORT).show()
                    autoConnectOrDiscover(forceShowPicker = true)
                }
            }
            .setNeutralButton("Direct Bluetooth") { _, _ ->
                switchToBluetoothModeInternal()
            }
            .setNegativeButton("Offline Mode") { _, _ ->
                loadOffline()
            }
            .show()
    }

    private fun loadServer(ip: String, port: Int = SERVER_PORT, name: String = "") {
        saveServerIp(ip)
        currentConnectedServerName = name
        currentConnectedServerPort = port
        val targetUrl = "http://$ip:$port/"
        webView.loadUrl(targetUrl)
    }

    private fun loadOffline() {
        webView.loadUrl("file:///android_asset/web/index.html")
        Toast.makeText(this, "Running in Offline / Customizer Mode", Toast.LENGTH_SHORT).show()
    }

    private fun saveServerIp(ip: String) {
        currentConnectedServerIp = ip
        prefs.edit().putString(KEY_LAST_IP, ip).apply()
    }

    private fun showServerAddressDialog(prefillIp: String) {
        if (isFinishing || isDestroyed) return
        val layout = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(50, 40, 50, 10)
        }

        val promptText = TextView(this).apply {
            text = "Enter the IP address of your Windows PC:"
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

        var dialogInstance: AlertDialog? = null

        val scanQrBtn = Button(this).apply {
            text = "📷 Scan Server QR Code Instead"
            setBackgroundColor(Color.parseColor("#1e293b"))
            setTextColor(Color.parseColor("#60a5fa"))
            setOnClickListener {
                dialogInstance?.dismiss()
                launchQrScanner()
            }
        }
        val btnParams = LinearLayout.LayoutParams(
            LinearLayout.LayoutParams.MATCH_PARENT,
            LinearLayout.LayoutParams.WRAP_CONTENT
        ).apply {
            topMargin = 20
        }
        layout.addView(scanQrBtn, btnParams)

        dialogInstance = AlertDialog.Builder(this)
            .setTitle("OmniPad Connection")
            .setView(layout)
            .setPositiveButton("Connect") { _, _ ->
                val ip = input.text.toString().trim()
                if (ip.isNotEmpty()) {
                    loadServer(ip)
                }
            }
            .setNeutralButton("Scan Wi-Fi") { _, _ ->
                Toast.makeText(this, "Scanning Wi-Fi for PC servers...", Toast.LENGTH_SHORT).show()
                autoConnectOrDiscover(forceShowPicker = true)
            }
            .setNegativeButton("Offline Mode") { _, _ ->
                loadOffline()
            }
            .setCancelable(true)
            .show()
    }

    fun launchQrScanner() {
        try {
            val options = ScanOptions().apply {
                setDesiredBarcodeFormats(ScanOptions.QR_CODE)
                setPrompt("Scan OmniPad Server QR Code on PC Screen")
                setCameraId(0)
                setBeepEnabled(true)
                setBarcodeImageEnabled(false)
                setOrientationLocked(false)
                // Enable mixed scan (both standard black-on-white and inverted white-on-black terminal QR codes)
                addExtra("SCAN_TYPE", "MIXED_SCAN")
                addExtra("TRY_HARDER", true)
            }
            qrScannerLauncher.launch(options)
        } catch (e: Exception) {
            Toast.makeText(this, "Could not open camera scanner: ${e.message}", Toast.LENGTH_SHORT).show()
        }
    }

    private fun handleScannedQr(raw: String) {
        val server = parseServerUri(raw)
        if (server != null) {
            runOnUiThread {
                Toast.makeText(this, "QR Code Scanned! Connecting to ${server.displayName}...", Toast.LENGTH_SHORT).show()
                loadServer(server.ip, server.port, server.name)
            }
        } else {
            runOnUiThread {
                Toast.makeText(this, "Invalid QR code: $raw", Toast.LENGTH_LONG).show()
            }
        }
    }

    private fun parseServerUri(raw: String): DiscoveredServer? {
        val trimmed = raw.trim()
        if (trimmed.isEmpty()) return null

        try {
            val uri = Uri.parse(trimmed)
            val scheme = uri.scheme?.lowercase()

            if (scheme == "omnipad") {
                val ipParam = uri.getQueryParameter("ip")
                if (!ipParam.isNullOrBlank()) {
                    val port = uri.getQueryParameter("port")?.toIntOrNull() ?: SERVER_PORT
                    return DiscoveredServer(ipParam, "QR Server", port)
                }
                val host = uri.host
                if (!host.isNullOrBlank()) {
                    val port = if (uri.port > 0) uri.port else SERVER_PORT
                    return DiscoveredServer(host, "QR Server", port)
                }
                val ssp = uri.schemeSpecificPart?.trimStart('/')
                if (!ssp.isNullOrBlank()) {
                    return parseHostAndPort(ssp)
                }
            }

            if (scheme == "http" || scheme == "https") {
                val host = uri.host
                if (!host.isNullOrBlank()) {
                    val port = if (uri.port > 0) uri.port else SERVER_PORT
                    return DiscoveredServer(host, "QR Server", port)
                }
            }
        } catch (_: Exception) {}

        return parseHostAndPort(trimmed)
    }

    private fun parseHostAndPort(str: String): DiscoveredServer? {
        val clean = str.replace(Regex("^[a-zA-Z]+://"), "").trimEnd('/')
        val parts = clean.split(":")
        if (parts.isNotEmpty() && parts[0].isNotBlank()) {
            val host = parts[0].trim()
            val port = if (parts.size > 1) parts[1].toIntOrNull() ?: SERVER_PORT else SERVER_PORT
            if (host.matches(Regex("^[0-9a-zA-Z.-]+$"))) {
                return DiscoveredServer(host, "QR Server", port)
            }
        }
        return null
    }

    private fun handleIncomingIntent(intent: Intent?): Boolean {
        if (intent == null) return false
        val action = intent.action
        val data = intent.data
        if (Intent.ACTION_VIEW == action && data != null) {
            val dataStr = data.toString()
            if (dataStr.contains("scan", ignoreCase = true)) {
                runOnUiThread {
                    launchQrScanner()
                }
                return true
            }
            val server = parseServerUri(dataStr)
            if (server != null) {
                runOnUiThread {
                    Toast.makeText(this, "Connecting to ${server.displayName} via link...", Toast.LENGTH_SHORT).show()
                    loadServer(server.ip, server.port, server.name)
                }
                return true
            }
        }
        return false
    }

    private fun checkAndRequestBluetoothPermissions(): Boolean {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
            val permissions = arrayOf(
                android.Manifest.permission.BLUETOOTH_CONNECT,
                android.Manifest.permission.BLUETOOTH_ADVERTISE
            )
            val missing = permissions.filter {
                checkSelfPermission(it) != PackageManager.PERMISSION_GRANTED
            }
            if (missing.isNotEmpty()) {
                runOnUiThread {
                    requestPermissions(missing.toTypedArray(), RC_BLUETOOTH_PERMISSIONS)
                }
                return false
            }
        }
        return true
    }

    override fun onRequestPermissionsResult(
        requestCode: Int,
        permissions: Array<out String>,
        grantResults: IntArray
    ) {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults)
        if (requestCode == RC_BLUETOOTH_PERMISSIONS) {
            val allGranted = grantResults.isNotEmpty() && grantResults.all { it == PackageManager.PERMISSION_GRANTED }
            if (allGranted) {
                val bm = getSystemService(Context.BLUETOOTH_SERVICE) as? BluetoothManager
                bm?.adapter?.let { bluetoothHidManager?.start(it) }
            } else {
                Toast.makeText(this, "Bluetooth permissions needed for Direct Gamepad mode", Toast.LENGTH_SHORT).show()
            }
        } else if (requestCode == RC_MIC_PERMISSION) {
            val granted = grantResults.isNotEmpty() && grantResults[0] == PackageManager.PERMISSION_GRANTED
            if (granted) {
                Toast.makeText(this, "Microphone access granted", Toast.LENGTH_SHORT).show()
                webView.evaluateJavascript("if (window.omnipadApp && window.omnipadApp.mic) window.omnipadApp.mic.start();", null)
            } else {
                Toast.makeText(this, "Microphone permission required for streaming", Toast.LENGTH_SHORT).show()
            }
        }
    }

    private fun switchToBluetoothModeInternal() {
        loadOffline()
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.P) {
            if (checkAndRequestBluetoothPermissions()) {
                val bm = getSystemService(Context.BLUETOOTH_SERVICE) as? BluetoothManager
                val adapter = bm?.adapter
                if (adapter != null && adapter.isEnabled) {
                    bluetoothHidManager?.start(adapter)
                } else {
                    Toast.makeText(this, "Please turn on Bluetooth in Settings", Toast.LENGTH_SHORT).show()
                }
            }
        } else {
            Toast.makeText(this, "Direct Bluetooth Gamepad requires Android 9+", Toast.LENGTH_LONG).show()
        }
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
        bluetoothHidManager?.stop()
        try {
            isNativeRecording = false
            nativeAudioRecord?.stop()
            nativeAudioRecord?.release()
        } catch (_: Exception) {}
        nativeAudioRecord = null
        nativeMicThread = null
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
        fun isUsbConnected(): Boolean {
            return isUsbCableConnected()
        }

        @JavascriptInterface
        fun scanAndSelectServer() {
            runOnUiThread {
                Toast.makeText(this@MainActivity, "Scanning for OmniPad PC servers...", Toast.LENGTH_SHORT).show()
                autoConnectOrDiscover(forceShowPicker = true)
            }
        }

        @JavascriptInterface
        fun getBluetoothStatus(): String {
            return bluetoothStatus
        }

        @JavascriptInterface
        fun getBluetoothHost(): String {
            return if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.P) {
                bluetoothHidManager?.getConnectedHostName() ?: ""
            } else ""
        }

        @JavascriptInterface
        fun startBluetoothHid(): Boolean {
            if (Build.VERSION.SDK_INT < Build.VERSION_CODES.P) return false
            if (!checkAndRequestBluetoothPermissions()) return false
            val bm = getSystemService(Context.BLUETOOTH_SERVICE) as? BluetoothManager
            val adapter = bm?.adapter ?: return false
            if (!adapter.isEnabled) {
                runOnUiThread {
                    Toast.makeText(this@MainActivity, "Please turn on Bluetooth in Android Settings", Toast.LENGTH_SHORT).show()
                }
                return false
            }
            return bluetoothHidManager?.start(adapter) ?: false
        }

        @JavascriptInterface
        fun stopBluetoothHid() {
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.P) {
                bluetoothHidManager?.stop()
            }
        }

        @JavascriptInterface
        fun sendBluetoothReport(buttons: Int, lx: Int, ly: Int, rx: Int, ry: Int, lt: Int, rt: Int): Boolean {
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.P) {
                return bluetoothHidManager?.sendReport(buttons, lx, ly, rx, ry, lt, rt) ?: false
            }
            return false
        }

        @JavascriptInterface
        fun switchToBluetoothMode() {
            runOnUiThread {
                switchToBluetoothModeInternal()
            }
        }

        @JavascriptInterface
        fun startNativeMic(): Boolean {
            if (isNativeRecording) return true
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
                if (checkSelfPermission(android.Manifest.permission.RECORD_AUDIO) != PackageManager.PERMISSION_GRANTED) {
                    runOnUiThread {
                        requestPermissions(arrayOf(android.Manifest.permission.RECORD_AUDIO), RC_MIC_PERMISSION)
                    }
                    return false
                }
            }

            return try {
                val sampleRate = 16000
                val channelConfig = AudioFormat.CHANNEL_IN_MONO
                val audioFormat = AudioFormat.ENCODING_PCM_16BIT
                val minBufSize = AudioRecord.getMinBufferSize(sampleRate, channelConfig, audioFormat)
                val bufferSize = Math.max(minBufSize, 4096)

                nativeAudioRecord = AudioRecord(
                    MediaRecorder.AudioSource.MIC,
                    sampleRate,
                    channelConfig,
                    audioFormat,
                    bufferSize
                )

                if (nativeAudioRecord?.state != AudioRecord.STATE_INITIALIZED) {
                    try {
                        nativeAudioRecord?.release()
                    } catch (_: Exception) {}
                    nativeAudioRecord = null
                    return false
                }

                isNativeRecording = true
                isNativeMicMuted = false
                nativeAudioRecord?.startRecording()

                var targetIp = currentConnectedServerIp
                if (targetIp.isNullOrBlank() || targetIp == "localhost") {
                    targetIp = "127.0.0.1"
                }

                nativeMicThread = Thread {
                    var socket: Socket? = null
                    try {
                        socket = Socket(targetIp, 27503)
                        socket.tcpNoDelay = true
                        val outputStream = socket.getOutputStream()
                        val pcmBuffer = ByteArray(2048)

                        while (isNativeRecording) {
                            val record = nativeAudioRecord ?: break
                            val read = record.read(pcmBuffer, 0, pcmBuffer.size)
                            if (read > 0) {
                                if (!isNativeMicMuted) {
                                    outputStream.write(pcmBuffer, 0, read)
                                }
                                var sumSquares = 0.0
                                var samples = 0
                                var i = 0
                                while (i < read - 1) {
                                    val sample = (pcmBuffer[i].toInt() and 0xFF) or (pcmBuffer[i + 1].toInt() shl 8)
                                    val norm = sample.toShort().toFloat() / 32768f
                                    sumSquares += norm * norm
                                    samples++
                                    i += 2
                                }
                                if (samples > 0) {
                                    val rms = Math.sqrt(sumSquares / samples).toFloat()
                                    nativeMicLevel = Math.min(1.0f, rms * 4.0f)
                                }
                            } else {
                                Thread.sleep(10)
                            }
                        }
                        outputStream.flush()
                    } catch (_: Exception) {
                    } finally {
                        try { socket?.close() } catch (_: Exception) {}
                        try {
                            nativeAudioRecord?.stop()
                            nativeAudioRecord?.release()
                        } catch (_: Exception) {}
                        nativeAudioRecord = null
                        nativeMicThread = null
                        isNativeRecording = false
                        nativeMicLevel = 0f
                    }
                }.apply {
                    priority = Thread.MAX_PRIORITY
                    start()
                }
                true
            } catch (e: Exception) {
                try {
                    nativeAudioRecord?.stop()
                    nativeAudioRecord?.release()
                } catch (_: Exception) {}
                nativeAudioRecord = null
                nativeMicThread = null
                isNativeRecording = false
                false
            }
        }

        @JavascriptInterface
        fun stopNativeMic(): Boolean {
            isNativeRecording = false
            try {
                nativeAudioRecord?.stop()
                nativeAudioRecord?.release()
            } catch (_: Exception) {}
            nativeAudioRecord = null
            nativeMicThread = null
            nativeMicLevel = 0f
            return true
        }

        @JavascriptInterface
        fun isNativeMicStreaming(): Boolean {
            return isNativeRecording
        }

        @JavascriptInterface
        fun getNativeMicLevel(): Float {
            return nativeMicLevel
        }

        @JavascriptInterface
        fun setNativeMicMuted(muted: Boolean) {
            isNativeMicMuted = muted
            if (muted) nativeMicLevel = 0f
        }

        @JavascriptInterface
        fun scanQrCode() {
            runOnUiThread {
                launchQrScanner()
            }
        }
    }
}

