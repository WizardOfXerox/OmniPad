package com.omnipad.client.protocol

object Protocol {
    const val MAGIC_BYTE: Byte = 0xDA.toByte()
    const val VERSION: Byte = 1

    const val DEFAULT_INPUT_PORT = 27500
    const val DISCOVERY_PORT = 27501
    const val DEFAULT_WEB_PORT = 27502
    const val DEFAULT_DSU_PORT = 26760

    const val INPUT_PACKET_SIZE = 20
    const val SESSION_MESSAGE_SIZE = 4
    const val RUMBLE_MESSAGE_SIZE = 6
    const val MOTION_PACKET_SIZE = 36
    const val TOUCHPAD_PACKET_SIZE = 13

    const val NO_PAD: Byte = 0xFF.toByte()

    const val MSG_INPUT: Byte = 0x01
    const val MSG_HELLO: Byte = 0x02
    const val MSG_WELCOME: Byte = 0x03
    const val MSG_BYE: Byte = 0x04
    const val MSG_RUMBLE: Byte = 0x05
    const val MSG_DISCOVER: Byte = 0x06
    const val MSG_MOTION: Byte = 0x10
    const val MSG_TOUCHPAD: Byte = 0x11
    const val MSG_SET_CONTROLLER_TYPE: Byte = 0x12
    const val MSG_ACTIVE_PROFILE: Byte = 0x13

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
    const val BTN_TOUCHPAD: Short = 0x0800.toShort()
    const val BTN_A: Short = 0x1000.toShort()
    const val BTN_B: Short = 0x2000.toShort()
    const val BTN_X: Short = 0x4000.toShort()
    const val BTN_Y: Short = 0x8000.toShort()
}



data class DiscoveredServer(
    val ip: String,
    val name: String,
    val port: Int = Protocol.DEFAULT_WEB_PORT
) {
    val displayName: String
        get() = if (name.isNotBlank()) "$name ($ip)" else ip
}
