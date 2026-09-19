package com.omnipad.client.transport

import android.bluetooth.BluetoothAdapter
import android.bluetooth.BluetoothDevice
import android.bluetooth.BluetoothHidDevice
import android.bluetooth.BluetoothHidDeviceAppSdpSettings
import android.bluetooth.BluetoothProfile
import android.content.Context
import android.os.Build
import androidx.annotation.RequiresApi
import java.util.concurrent.Executors

/**
 * Driverless Bluetooth HID Gamepad Manager (Android 9+ / API 28+)
 * Registers the smartphone as a genuine hardware Bluetooth Gamepad with Windows.
 */
@RequiresApi(Build.VERSION_CODES.P)
class BluetoothHidManager(private val context: Context) {

    private var hidDevice: BluetoothHidDevice? = null
    private var connectedHost: BluetoothDevice? = null
    private val executor = Executors.newSingleThreadExecutor()

    // Standard Gamepad HID Descriptor: 16 buttons, 4 16-bit analog axes, 2 8-bit triggers
    companion object {
        val HID_DESCRIPTOR = byteArrayOf(
            0x05.toByte(), 0x01.toByte(),        // USAGE_PAGE (Generic Desktop)
            0x09.toByte(), 0x05.toByte(),        // USAGE (Gamepad)
            0xa1.toByte(), 0x01.toByte(),        // COLLECTION (Application)
            0x85.toByte(), 0x01.toByte(),        //   REPORT_ID (1)
            // 16 Buttons
            0x05.toByte(), 0x09.toByte(),        //   USAGE_PAGE (Button)
            0x19.toByte(), 0x01.toByte(),        //   USAGE_MINIMUM (Button 1)
            0x29.toByte(), 0x10.toByte(),        //   USAGE_MAXIMUM (Button 16)
            0x15.toByte(), 0x00.toByte(),        //   LOGICAL_MINIMUM (0)
            0x25.toByte(), 0x01.toByte(),        //   LOGICAL_MAXIMUM (1)
            0x75.toByte(), 0x01.toByte(),        //   REPORT_SIZE (1)
            0x95.toByte(), 0x10.toByte(),        //   REPORT_COUNT (16)
            0x81.toByte(), 0x02.toByte(),        //   INPUT (Data,Var,Abs)
            // Left & Right Joysticks (X, Y, Z, Rz)
            0x05.toByte(), 0x01.toByte(),        //   USAGE_PAGE (Generic Desktop)
            0x09.toByte(), 0x30.toByte(),        //   USAGE (X)
            0x09.toByte(), 0x31.toByte(),        //   USAGE (Y)
            0x09.toByte(), 0x32.toByte(),        //   USAGE (Z - Right Stick X)
            0x09.toByte(), 0x35.toByte(),        //   USAGE (Rz - Right Stick Y)
            0x16.toByte(), 0x00.toByte(), 0x80.toByte(), // LOGICAL_MINIMUM (-32768)
            0x26.toByte(), 0xff.toByte(), 0x7f.toByte(), // LOGICAL_MAXIMUM (32767)
            0x75.toByte(), 0x10.toByte(),        //   REPORT_SIZE (16)
            0x95.toByte(), 0x04.toByte(),        //   REPORT_COUNT (4)
            0x81.toByte(), 0x02.toByte(),        //   INPUT (Data,Var,Abs)
            0xc0.toByte()                        // END_COLLECTION
        )
    }

    private val callback = object : BluetoothHidDevice.Callback() {
        override fun onConnectionStateChanged(device: BluetoothDevice?, state: Int) {
            if (state == BluetoothProfile.STATE_CONNECTED) {
                connectedHost = device
            } else if (state == BluetoothProfile.STATE_DISCONNECTED) {
                connectedHost = null
            }
        }
    }

    fun start(bluetoothAdapter: BluetoothAdapter) {
        bluetoothAdapter.getProfileProxy(context, object : BluetoothProfile.ServiceListener {
            override fun onServiceConnected(profile: Int, proxy: BluetoothProfile) {
                if (profile == BluetoothProfile.HID_DEVICE) {
                    hidDevice = proxy as BluetoothHidDevice
                    val sdp = BluetoothHidDeviceAppSdpSettings(
                        "OmniPad Bluetooth Gamepad",
                        "Virtual Gamepad Controller",
                        "OmniPad",
                        BluetoothHidDevice.SUBCLASS1_COMBO,
                        HID_DESCRIPTOR
                    )
                    hidDevice?.registerApp(sdp, null, null, executor, callback)
                }
            }

            override fun onServiceDisconnected(profile: Int) {
                hidDevice = null
            }
        }, BluetoothProfile.HID_DEVICE)
    }

    fun sendReport(buttons: Short, lx: Short, ly: Short, rx: Short, ry: Short) {
        val host = connectedHost ?: return
        val report = ByteArray(10)
        report[0] = (buttons.toInt() and 0xFF).toByte()
        report[1] = ((buttons.toInt() shr 8) and 0xFF).toByte()
        report[2] = (lx.toInt() and 0xFF).toByte()
        report[3] = ((lx.toInt() shr 8) and 0xFF).toByte()
        report[4] = (ly.toInt() and 0xFF).toByte()
        report[5] = ((ly.toInt() shr 8) and 0xFF).toByte()
        report[6] = (rx.toInt() and 0xFF).toByte()
        report[7] = ((rx.toInt() shr 8) and 0xFF).toByte()
        report[8] = (ry.toInt() and 0xFF).toByte()
        report[9] = ((ry.toInt() shr 8) and 0xFF).toByte()

        hidDevice?.sendReport(host, 1, report)
    }

    fun stop() {
        hidDevice?.unregisterApp()
    }
}
