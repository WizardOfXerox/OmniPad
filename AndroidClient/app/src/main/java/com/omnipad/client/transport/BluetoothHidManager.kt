package com.omnipad.client.transport

import android.annotation.SuppressLint
import android.bluetooth.BluetoothAdapter
import android.bluetooth.BluetoothDevice
import android.bluetooth.BluetoothHidDevice
import android.bluetooth.BluetoothHidDeviceAppSdpSettings
import android.bluetooth.BluetoothProfile
import android.content.Context
import android.os.Build
import android.util.Log
import androidx.annotation.RequiresApi
import java.util.concurrent.Executors

/**
 * Driverless Bluetooth HID Gamepad Manager (Android 9+ / API 28+)
 * Registers the smartphone as a genuine hardware Bluetooth Gamepad with Windows, Mac, Linux, Android TV, etc.
 */
@RequiresApi(Build.VERSION_CODES.P)
class BluetoothHidManager(private val context: Context) {

    @Volatile private var hidDevice: BluetoothHidDevice? = null
    @Volatile private var connectedHost: BluetoothDevice? = null
    @Volatile private var isRegistered = false
    private var bluetoothAdapter: BluetoothAdapter? = null
    private val executor = Executors.newSingleThreadExecutor()

    var onStatusChanged: ((String) -> Unit)? = null

    companion object {
        private const val TAG = "OmniPadBluetoothHid"

        // Universal Gamepad HID Descriptor:
        // - Report ID: 1
        // - 16 Digital Buttons (A, B, X, Y, LB, RB, LS, RS, D-Pad directions, Start, Back, Guide, Touchpad click)
        // - 4 16-bit Joysticks axes: X, Y (Left Stick), Z, Rz (Right Stick) [-32768 to 32767]
        // - 2 8-bit Triggers: Rx (Left Trigger / Brake), Ry (Right Trigger / Accelerator) [0 to 255]
        val HID_DESCRIPTOR = byteArrayOf(
            0x05.toByte(), 0x01.toByte(),        // USAGE_PAGE (Generic Desktop)
            0x09.toByte(), 0x05.toByte(),        // USAGE (Gamepad)
            0xa1.toByte(), 0x01.toByte(),        // COLLECTION (Application)
            0x85.toByte(), 0x01.toByte(),        //   REPORT_ID (1)

            // 16 Buttons (1 bit each = 2 bytes)
            0x05.toByte(), 0x09.toByte(),        //   USAGE_PAGE (Button)
            0x19.toByte(), 0x01.toByte(),        //   USAGE_MINIMUM (Button 1)
            0x29.toByte(), 0x10.toByte(),        //   USAGE_MAXIMUM (Button 16)
            0x15.toByte(), 0x00.toByte(),        //   LOGICAL_MINIMUM (0)
            0x25.toByte(), 0x01.toByte(),        //   LOGICAL_MAXIMUM (1)
            0x75.toByte(), 0x01.toByte(),        //   REPORT_SIZE (1)
            0x95.toByte(), 0x10.toByte(),        //   REPORT_COUNT (16)
            0x81.toByte(), 0x02.toByte(),        //   INPUT (Data,Var,Abs)

            // Left Stick: X, Y (16-bit signed each = 4 bytes)
            0x05.toByte(), 0x01.toByte(),        //   USAGE_PAGE (Generic Desktop)
            0x09.toByte(), 0x30.toByte(),        //   USAGE (X)
            0x09.toByte(), 0x31.toByte(),        //   USAGE (Y)
            // Right Stick: Z, Rz (16-bit signed each = 4 bytes)
            0x09.toByte(), 0x32.toByte(),        //   USAGE (Z - Right Stick X)
            0x09.toByte(), 0x35.toByte(),        //   USAGE (Rz - Right Stick Y)
            0x16.toByte(), 0x00.toByte(), 0x80.toByte(), // LOGICAL_MINIMUM (-32768)
            0x26.toByte(), 0xff.toByte(), 0x7f.toByte(), // LOGICAL_MAXIMUM (32767)
            0x75.toByte(), 0x10.toByte(),        //   REPORT_SIZE (16)
            0x95.toByte(), 0x04.toByte(),        //   REPORT_COUNT (4)
            0x81.toByte(), 0x02.toByte(),        //   INPUT (Data,Var,Abs)

            // Triggers: Rx (LT), Ry (RT) (8-bit each = 2 bytes)
            0x05.toByte(), 0x01.toByte(),        //   USAGE_PAGE (Generic Desktop)
            0x09.toByte(), 0x33.toByte(),        //   USAGE (Rx - Left Trigger)
            0x09.toByte(), 0x34.toByte(),        //   USAGE (Ry - Right Trigger)
            0x15.toByte(), 0x00.toByte(),        //   LOGICAL_MINIMUM (0)
            0x26.toByte(), 0xff.toByte(), 0x00.toByte(), // LOGICAL_MAXIMUM (255)
            0x75.toByte(), 0x08.toByte(),        //   REPORT_SIZE (8)
            0x95.toByte(), 0x02.toByte(),        //   REPORT_COUNT (2)
            0x81.toByte(), 0x02.toByte(),        //   INPUT (Data,Var,Abs)

            0xc0.toByte()                        // END_COLLECTION
        )
    }

    private val callback = object : BluetoothHidDevice.Callback() {
        @SuppressLint("MissingPermission")
        override fun onAppStatusChanged(pluggedDevice: BluetoothDevice?, registered: Boolean) {
            Log.d(TAG, "Bluetooth HID SDP App Status: registered=$registered")
            isRegistered = registered
            if (pluggedDevice != null) {
                connectedHost = pluggedDevice
                onStatusChanged?.invoke("connected")
            } else {
                val connected = try { hidDevice?.connectedDevices } catch (_: Exception) { null }
                if (!connected.isNullOrEmpty()) {
                    connectedHost = connected.first()
                    onStatusChanged?.invoke("connected")
                } else {
                    onStatusChanged?.invoke(if (registered) "ready" else "unregistered")
                }
            }
        }

        @SuppressLint("MissingPermission")
        override fun onConnectionStateChanged(device: BluetoothDevice?, state: Int) {
            val deviceName = device?.name ?: device?.address ?: "Host"
            Log.d(TAG, "Bluetooth HID Connection State: $deviceName -> $state")
            if (state == BluetoothProfile.STATE_CONNECTED) {
                connectedHost = device
                onStatusChanged?.invoke("connected")
            } else if (state == BluetoothProfile.STATE_DISCONNECTED) {
                connectedHost = null
                onStatusChanged?.invoke(if (isRegistered) "ready" else "disconnected")
            }
        }
    }

    @SuppressLint("MissingPermission")
    fun start(bluetoothAdapter: BluetoothAdapter): Boolean {
        this.bluetoothAdapter = bluetoothAdapter
        if (!bluetoothAdapter.isEnabled) {
            Log.w(TAG, "Bluetooth is disabled on device.")
            onStatusChanged?.invoke("disabled")
            return false
        }

        onStatusChanged?.invoke("initializing")

        return try {
            bluetoothAdapter.getProfileProxy(context, object : BluetoothProfile.ServiceListener {
                override fun onServiceConnected(profile: Int, proxy: BluetoothProfile) {
                    if (profile == BluetoothProfile.HID_DEVICE) {
                        hidDevice = proxy as BluetoothHidDevice
                        val sdp = BluetoothHidDeviceAppSdpSettings(
                            "OmniPad Bluetooth Gamepad",
                            "Universal Gamepad Controller",
                            "OmniPad",
                            BluetoothHidDevice.SUBCLASS2_GAMEPAD,
                            HID_DESCRIPTOR
                        )
                        try {
                            hidDevice?.registerApp(sdp, null, null, executor, callback)
                            Log.i(TAG, "Bluetooth HID Gamepad SDP application registered successfully.")
                        } catch (e: Exception) {
                            Log.e(TAG, "Error registering Bluetooth HID App SDP", e)
                            onStatusChanged?.invoke("error")
                        }
                    }
                }

                override fun onServiceDisconnected(profile: Int) {
                    Log.i(TAG, "Bluetooth HID profile service disconnected.")
                    hidDevice = null
                    isRegistered = false
                    connectedHost = null
                    onStatusChanged?.invoke("disconnected")
                }
            }, BluetoothProfile.HID_DEVICE)
            true
        } catch (e: Exception) {
            Log.e(TAG, "Failed to get Bluetooth HID profile proxy", e)
            onStatusChanged?.invoke("unsupported")
            false
        }
    }

    @SuppressLint("MissingPermission")
    fun sendReport(buttons: Int, lx: Int, ly: Int, rx: Int, ry: Int, lt: Int, rt: Int): Boolean {
        val host = connectedHost ?: return false
        val dev = hidDevice ?: return false

        // Remap XInput bitmask to standard USB HID Gamepad button descriptor:
        // A -> 1 (bit 0), B -> 2 (bit 1), X -> 3 (bit 2), Y -> 4 (bit 3)
        // LB -> 5 (bit 4), RB -> 6 (bit 5), Back -> 7 (bit 6), Start -> 8 (bit 7)
        // LS -> 9 (bit 8), RS -> 10 (bit 9), DUp -> 11 (bit 10), DDown -> 12 (bit 11)
        // DLeft -> 13 (bit 12), DRight -> 14 (bit 13), Guide -> 15 (bit 14), Touchpad -> 16 (bit 15)
        var hidButtons = 0
        if ((buttons and 0x1000) != 0) hidButtons = hidButtons or (1 shl 0)  // A
        if ((buttons and 0x2000) != 0) hidButtons = hidButtons or (1 shl 1)  // B
        if ((buttons and 0x4000) != 0) hidButtons = hidButtons or (1 shl 2)  // X
        if ((buttons and 0x8000) != 0) hidButtons = hidButtons or (1 shl 3)  // Y
        if ((buttons and 0x0100) != 0) hidButtons = hidButtons or (1 shl 4)  // LB
        if ((buttons and 0x0200) != 0) hidButtons = hidButtons or (1 shl 5)  // RB
        if ((buttons and 0x0020) != 0) hidButtons = hidButtons or (1 shl 6)  // Back
        if ((buttons and 0x0010) != 0) hidButtons = hidButtons or (1 shl 7)  // Start
        if ((buttons and 0x0040) != 0) hidButtons = hidButtons or (1 shl 8)  // LS
        if ((buttons and 0x0080) != 0) hidButtons = hidButtons or (1 shl 9)  // RS
        if ((buttons and 0x0001) != 0) hidButtons = hidButtons or (1 shl 10) // DUp
        if ((buttons and 0x0002) != 0) hidButtons = hidButtons or (1 shl 11) // DDown
        if ((buttons and 0x0004) != 0) hidButtons = hidButtons or (1 shl 12) // DLeft
        if ((buttons and 0x0008) != 0) hidButtons = hidButtons or (1 shl 13) // DRight
        if ((buttons and 0x0400) != 0) hidButtons = hidButtons or (1 shl 14) // Guide
        if ((buttons and 0x0800) != 0) hidButtons = hidButtons or (1 shl 15) // Touchpad

        // Standard USB HID Gamepad coordinate convention: UP is negative Y (-32768)
        val invLy = (-ly).coerceIn(-32768, 32767)
        val invRy = (-ry).coerceIn(-32768, 32767)

        // 12-byte payload matching universal HID descriptor
        val report = ByteArray(12)
        // Buttons (16-bit mapped)
        report[0] = (hidButtons and 0xFF).toByte()
        report[1] = ((hidButtons shr 8) and 0xFF).toByte()
        // Left Stick (16-bit signed)
        report[2] = (lx and 0xFF).toByte()
        report[3] = ((lx shr 8) and 0xFF).toByte()
        report[4] = (invLy and 0xFF).toByte()
        report[5] = ((invLy shr 8) and 0xFF).toByte()
        // Right Stick (16-bit signed)
        report[6] = (rx and 0xFF).toByte()
        report[7] = ((rx shr 8) and 0xFF).toByte()
        report[8] = (invRy and 0xFF).toByte()
        report[9] = ((invRy shr 8) and 0xFF).toByte()
        // Triggers (8-bit unsigned 0..255)
        report[10] = (lt.coerceIn(0, 255)).toByte()
        report[11] = (rt.coerceIn(0, 255)).toByte()

        return try {
            dev.sendReport(host, 1, report)
        } catch (e: Exception) {
            false
        }
    }

    @SuppressLint("MissingPermission")
    fun getConnectedHostName(): String? {
        return connectedHost?.name ?: connectedHost?.address
    }

    fun isConnected(): Boolean = connectedHost != null
    fun isAppRegistered(): Boolean = isRegistered

    @SuppressLint("MissingPermission")
    fun stop() {
        try {
            hidDevice?.unregisterApp()
        } catch (_: Exception) { }
        try {
            hidDevice?.let { dev ->
                bluetoothAdapter?.closeProfileProxy(BluetoothProfile.HID_DEVICE, dev)
            }
        } catch (_: Exception) { }
        hidDevice = null
        isRegistered = false
        connectedHost = null
        try {
            executor.shutdown()
        } catch (_: Exception) { }
        onStatusChanged?.invoke("stopped")
    }
}
