package com.omnipad.client.sensors

import android.content.Context
import android.hardware.Sensor
import android.hardware.SensorEvent
import android.hardware.SensorEventListener
import android.hardware.SensorManager

data class RawMotion(
    val timestampUs: Long,
    val accelX: Float,
    val accelY: Float,
    val accelZ: Float,
    val gyroX: Float,
    val gyroY: Float,
    val gyroZ: Float
)

class GyroController(
    context: Context,
    private val onMotion: ((dx: Float, dy: Float) -> Unit)? = null,
    private val onRawMotion: ((RawMotion) -> Unit)? = null
) : SensorEventListener {
    private val sensorManager = context.getSystemService(Context.SENSOR_SERVICE) as SensorManager
    private val gyroSensor = sensorManager.getDefaultSensor(Sensor.TYPE_GYROSCOPE)
    private val accelSensor = sensorManager.getDefaultSensor(Sensor.TYPE_ACCELEROMETER)
    private var isListening = false

    private var lastAx = 0f
    private var lastAy = 0f
    private var lastAz = 1f

    private val G = 9.80665f

    fun start() {
        if (!isListening) {
            gyroSensor?.let { sensorManager.registerListener(this, it, SensorManager.SENSOR_DELAY_GAME) }
            accelSensor?.let { sensorManager.registerListener(this, it, SensorManager.SENSOR_DELAY_GAME) }
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

        if (event.sensor.type == Sensor.TYPE_ACCELEROMETER) {
            lastAx = event.values[0] / G
            lastAy = event.values[1] / G
            lastAz = event.values[2] / G
        } else if (event.sensor.type == Sensor.TYPE_GYROSCOPE) {
            val gxRad = event.values[0]
            val gyRad = event.values[1]
            val gzRad = event.values[2]

            // Convert rad/s to deg/s
            val radToDeg = 180f / Math.PI.toFloat()
            val gx = gxRad * radToDeg
            val gy = gyRad * radToDeg
            val gz = gzRad * radToDeg

            onMotion?.invoke(-event.values[1], event.values[0])

            onRawMotion?.invoke(
                RawMotion(
                    timestampUs = event.timestamp / 1000L,
                    accelX = lastAx,
                    accelY = lastAy,
                    accelZ = lastAz,
                    gyroX = gx,
                    gyroY = gy,
                    gyroZ = gz
                )
            )
        }
    }

    override fun onAccuracyChanged(sensor: Sensor?, accuracy: Int) { }
}
