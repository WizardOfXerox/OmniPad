package com.omnipad.client.transport

import com.omnipad.client.protocol.PacketWriter
import com.omnipad.client.protocol.PadState
import com.omnipad.client.protocol.Protocol
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.util.concurrent.locks.LockSupport

class UdpTransport(
    private val host: String,
    private val port: Int = Protocol.DEFAULT_INPUT_PORT,
    private val rateHz: Int = 125,
    private val onRumble: ((large: Int, small: Int) -> Unit)? = null
) {
    private val socket = DatagramSocket()
    private val address: InetAddress = InetAddress.getByName(host)
    private val writer = PacketWriter()

    val state = PadState()
    private val stateLock = Any()

    @Volatile
    private var running = false
    @Volatile
    private var padSlot: Byte = Protocol.NO_PAD

    private var sequence = 0
    private var senderThread: Thread? = null
    private var receiverThread: Thread? = null

    fun updateState(block: (PadState) -> Unit) {
        synchronized(stateLock) {
            block(state)
        }
    }

    fun start() {
        if (running) return
        running = true

        receiverThread = Thread(::receiveLoop, "omnipad-recv").apply {
            isDaemon = true
            start()
        }

        senderThread = Thread(::sendLoop, "omnipad-send").apply {
            isDaemon = true
            priority = Thread.MAX_PRIORITY
            start()
        }
    }

    private fun sendLoop() {
        // 1. Handshake HELLO loop
        val helloBytes = writer.writeSession(Protocol.MSG_HELLO, Protocol.NO_PAD)
        val helloPacket = DatagramPacket(helloBytes, helloBytes.size, address, port)

        while (running && padSlot == Protocol.NO_PAD) {
            socket.send(helloPacket)
            Thread.sleep(150)
        }

        if (!running) return

        // 2. High-precision streaming loop
        val intervalNanos = 1_000_000_000L / rateHz
        var nextTick = System.nanoTime()

        while (running) {
            var packetBytes: ByteArray
            synchronized(stateLock) {
                sequence++
                packetBytes = writer.writeInput(padSlot, sequence, state)
            }

            val packet = DatagramPacket(packetBytes, packetBytes.size, address, port)
            socket.send(packet)

            nextTick += intervalNanos
            val sleepNanos = nextTick - System.nanoTime()
            if (sleepNanos > 0) {
                LockSupport.parkNanos(sleepNanos)
            } else {
                nextTick = System.nanoTime()
            }
        }

        // 3. Graceful shutdown: send neutral + BYE
        val neutralBytes = writer.writeInput(padSlot, sequence + 1, PadState())
        socket.send(DatagramPacket(neutralBytes, neutralBytes.size, address, port))

        val byeBytes = writer.writeSession(Protocol.MSG_BYE, padSlot)
        socket.send(DatagramPacket(byeBytes, byeBytes.size, address, port))
    }

    private fun receiveLoop() {
        val buffer = ByteArray(64)
        val packet = DatagramPacket(buffer, buffer.size)

        try {
            while (running) {
                socket.receive(packet)
                if (packet.length >= 4 && buffer[0] == Protocol.MAGIC_BYTE && buffer[1] == Protocol.VERSION) {
                    val type = buffer[2]
                    if (type == Protocol.MSG_WELCOME) {
                        padSlot = buffer[3]
                    } else if (type == Protocol.MSG_RUMBLE && packet.length >= 6) {
                        val large = buffer[4].toInt() and 0xFF
                        val small = buffer[5].toInt() and 0xFF
                        onRumble?.invoke(large, small)
                    }
                }
            }
        } catch (_: Exception) { }
    }

    fun stop() {
        if (!running) return
        running = false
        senderThread?.join(500)
        socket.close()
    }
}
