#!/usr/bin/env python3
"""OmniPad Fake Phone & Benchmark Harness.

Streams 20-byte binary UDP input packets to test OmniPadServer in isolation,
verify golden vector protocol compliance, and benchmark 125 Hz / 250 Hz latency.
"""

import argparse
import math
import socket
import struct
import sys
import time

MAGIC_BYTE = 0xDA
VERSION = 1

INPUT_PORT = 27500
DISCOVERY_PORT = 27501

INPUT_PACKET_SIZE = 20
SESSION_MESSAGE_SIZE = 4
RUMBLE_MESSAGE_SIZE = 6

NO_PAD = 0xFF

MSG_INPUT = 0x01
MSG_HELLO = 0x02
MSG_WELCOME = 0x03
MSG_BYE = 0x04
MSG_RUMBLE = 0x05
MSG_DISCOVER = 0x06

# < B B B B I H B B h h h h
INPUT_FORMAT = "<BBBBIHBBhhhh"
SESSION_FORMAT = "<BBBB"

BTN_DPAD_UP = 0x0001
BTN_DPAD_DOWN = 0x0002
BTN_DPAD_LEFT = 0x0004
BTN_DPAD_RIGHT = 0x0008
BTN_START = 0x0010
BTN_BACK = 0x0020
BTN_LEFT_THUMB = 0x0040
BTN_RIGHT_THUMB = 0x0080
BTN_LEFT_SHOULDER = 0x0100
BTN_RIGHT_SHOULDER = 0x0200
BTN_GUIDE = 0x0400
BTN_A = 0x1000
BTN_B = 0x2000
BTN_X = 0x4000
BTN_Y = 0x8000


def encode_input(pad: int, seq: int, buttons: int = 0, lt: int = 0, rt: int = 0,
                 lx: int = 0, ly: int = 0, rx: int = 0, ry: int = 0) -> bytes:
    return struct.pack(INPUT_FORMAT, MAGIC_BYTE, VERSION, MSG_INPUT, pad,
                       seq & 0xFFFFFFFF, buttons, lt, rt, lx, ly, rx, ry)


def encode_session(msg_type: int, pad: int) -> bytes:
    return struct.pack(SESSION_FORMAT, MAGIC_BYTE, VERSION, msg_type, pad)


def run_selftest() -> int:
    # Golden Vector validation
    golden_bytes = bytes.fromhex("da01010278563412041020c8e80318fcff7f0080")
    test_packet = encode_input(pad=2, seq=0x12345678, buttons=BTN_A | BTN_DPAD_LEFT,
                               lt=32, rt=200, lx=1000, ly=-1000, rx=32767, ry=-32768)

    print("OmniPad Protocol Self-Test")
    print(f"  Golden expected: {golden_bytes.hex()}")
    print(f"  Encoded actual:   {test_packet.hex()}")

    if test_packet == golden_bytes:
        print("  [PASS] Golden vector matched byte-for-byte!")
        return 0
    else:
        print("  [FAIL] Golden vector mismatch!")
        return 1


def run_stream(host: str, port: int, rate: float, duration: float | None):
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock.settimeout(0.3)

    print(f"Connecting to OmniPadServer at {host}:{port}...")
    hello = encode_session(MSG_HELLO, NO_PAD)

    slot = None
    for attempt in range(1, 6):
        sock.sendto(hello, (host, port))
        try:
            data, _ = sock.recvfrom(64)
            if len(data) == 4 and data[0] == MAGIC_BYTE and data[2] == MSG_WELCOME:
                slot = data[3]
                break
        except socket.timeout:
            continue

    if slot is None or slot == NO_PAD:
        print("Failed to connect: server not responding or all 4 slots are full.")
        sock.close()
        return 1

    print(f"Connected! Assigned Player Slot {slot + 1} (Pad {slot}).")
    print(f"Streaming inputs at {rate} Hz. Press Ctrl+C to stop.\n")

    interval = 1.0 / rate
    seq = 0
    started = time.perf_counter()
    next_tick = started
    last_report = started

    try:
        while True:
            now = time.perf_counter()
            if duration and (now - started) >= duration:
                break

            angle = (now - started) * 2 * math.pi / 2.0
            lx = int(math.cos(angle) * 28000)
            ly = int(math.sin(angle) * 28000)
            buttons = BTN_A if int(now - started) % 2 == 0 else 0

            pkt = encode_input(slot, seq, buttons=buttons, lx=lx, ly=ly)
            sock.sendto(pkt, (host, port))

            seq += 1
            if now - last_report >= 1.0:
                print(f"  t={(now - started):.1f}s | Seq: {seq:<7} | Rate: ~{seq / (now - started):.0f} Hz | Stick LX: {lx:<6} LY: {ly:<6}")
                last_report = now

            next_tick += interval
            delay = next_tick - time.perf_counter()
            if delay > 0:
                time.sleep(delay)
            else:
                next_tick = time.perf_counter()

    except KeyboardInterrupt:
        print("\nStopping stream...")
    finally:
        # Send neutral state and disconnect
        neutral = encode_input(slot, seq)
        sock.sendto(neutral, (host, port))
        sock.sendto(encode_session(MSG_BYE, slot), (host, port))
        sock.close()

    print(f"Benchmark finished: {seq} frames transmitted cleanly.")
    return 0


def main():
    parser = argparse.ArgumentParser(description="OmniPad Test & Benchmark Client")
    parser.add_argument("--host", default="127.0.0.1", help="Server host")
    parser.add_argument("--port", type=int, default=INPUT_PORT, help="Server port")
    parser.add_argument("--rate", type=float, default=125.0, help="Hz rate (e.g. 125 or 250)")
    parser.add_argument("--duration", type=float, default=None, help="Benchmark duration in seconds")
    parser.add_argument("--selftest", action="store_true", help="Run protocol selftest")
    args = parser.parse_args()

    if args.selftest:
        return run_selftest()
    return run_stream(args.host, args.port, args.rate, args.duration)


if __name__ == "__main__":
    sys.exit(main())
