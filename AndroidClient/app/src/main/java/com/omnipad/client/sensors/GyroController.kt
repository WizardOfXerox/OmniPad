package com.omnipad.client.sensors

import android.content.Context
import android.hardware.Sensor
import android.hardware.SensorEvent
import android.hardware.SensorEventListener
import android.hardware.SensorManager

class GyroController(context: Context, private val onMotion: (dx: Float, dy: Float) -> Unit) : SensorEventListener {
    private val sensorManager = context.getSystemService(Context.SENSOR_SERVICE) as SensorManager
    private val gyroSensor = sensorManager.getDefaultSensor(Sensor.TYPE_GYROSCOPE)
    private var isListening = false

    fun start() {
        if (gyroSensor != null && !isListening) {
            sensorManager.registerListener(this, gyroSensor, SensorManager.SENSOR_DELAY_GAME)
            isListening = true
        }
    }

    fun stop() {
        if (isListening) {
            sensorManager.unregisterListener(this)
            isListening = false
        }
    }

    override fun onSensorChanged(event: SensorEvent?) {
        if (event == null) return
        val x = event.values[0] // Pitch
        val y = event.values[1] // Roll
        onMotion(-y, x)
    }

    override fun onAccuracyChanged(sensor: Sensor?, accuracy: Int) { }
}
