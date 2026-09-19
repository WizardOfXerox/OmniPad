package com.omnipad.client.protocol

import java.nio.ByteBuffer
import java.nio.ByteOrder

object Protocol {
    const val MAGIC_BYTE: Byte = 0xDA.toByte()
    const val VERSION: Byte = 1

    const val DEFAULT_INPUT_PORT = 27500
    const val DISCOVERY_PORT = 27501

    const val INPUT_PACKET_SIZE = 20
    const val SESSION_MESSAGE_SIZE = 4
    const val RUMBLE_MESSAGE_SIZE = 6

    const val NO_PAD: Byte = 0xFF.toByte()

    const val MSG_INPUT: Byte = 0x01
    const val MSG_HELLO: Byte = 0x02
    const val MSG_WELCOME: Byte = 0x03
    const val MSG_BYE: Byte = 0x04
    const val MSG_RUMBLE: Byte = 0x05
    const val MSG_DISCOVER: Byte = 0x06

    // Button Bitmasks
    const val BTN_DPAD_UP: Short = 0x0001
    const val BTN_DPAD_DOWN: Short = 0x0002
    const val BTN_DPAD_LEFT: Short = 0x0004
    const val BTN_DPAD_RIGHT: Short = 0x0008
    const val BTN_START: Short = 0x0010
    const val BTN_BACK: Short = 0x0020
    const val BTN_LEFT_THUMB: Short = 0x0040
    const val BTN_RIGHT_THUMB: Short = 0x0080
    const val BTN_LEFT_SHOULDER: Short = 0x0100
    const val BTN_RIGHT_SHOULDER: Short = 0x0200
    const val BTN_GUIDE: Short = 0x0400
    const val BTN_A: Short = 0x1000.toShort()
    const val BTN_B: Short = 0x2000.toShort()
    const val BTN_X: Short = 0x4000.toShort()
    const val BTN_Y: Short = 0x8000.toShort()
}

data class PadState(
    var buttons: Short = 0,
    var leftTrigger: Byte = 0,
    var rightTrigger: Byte = 0,
    var thumbLX: Short = 0,
    var thumbLY: Short = 0,
    var thumbRX: Short = 0,
    var thumbRY: Short = 0
) {
    fun setButton(mask: Short, pressed: Boolean) {
        buttons = if (pressed) {
            (buttons.toInt() or mask.toInt()).toShort()
        } else {
            (buttons.toInt() and mask.toInt().inv()).toShort()
        }
    }
}

class PacketWriter {
    private val buffer = ByteBuffer.allocate(Protocol.INPUT_PACKET_SIZE).order(ByteOrder.LITTLE_ENDIAN)

    fun writeInput(pad: Byte, sequence: Int, state: PadState): ByteArray {
        buffer.clear()
        buffer.put(Protocol.MAGIC_BYTE)
        buffer.put(Protocol.VERSION)
        buffer.put(Protocol.MSG_INPUT)
        buffer.put(pad)
        buffer.putInt(sequence)
        buffer.putShort(state.buttons)
        buffer.put(state.leftTrigger)
        buffer.put(state.rightTrigger)
        buffer.putShort(state.thumbLX)
        buffer.putShort(state.thumbLY)
        buffer.putShort(state.thumbRX)
        buffer.putShort(state.thumbRY)
        return buffer.array()
    }

    fun writeSession(msgType: Byte, pad: Byte): ByteArray {
        val sessionBuf = ByteBuffer.allocate(Protocol.SESSION_MESSAGE_SIZE).order(ByteOrder.LITTLE_ENDIAN)
        sessionBuf.put(Protocol.MAGIC_BYTE)
        sessionBuf.put(Protocol.VERSION)
        sessionBuf.put(msgType)
        sessionBuf.put(pad)
        return sessionBuf.array()
    }
}
