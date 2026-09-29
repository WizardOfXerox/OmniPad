import socket
import struct
import os
import sys

MAGIC = 0xDA
VERSION = 1
MSG_DISCOVER = 0x06
NO_PAD = 0xFF

sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
sock.setsockopt(socket.SOL_SOCKET, socket.SO_BROADCAST, 1)
sock.settimeout(2.0)

packet = bytes([MAGIC, VERSION, MSG_DISCOVER, NO_PAD])
print('Sending discovery packet to 127.0.0.1:27501...')
sock.sendto(packet, ('127.0.0.1', 27501))

try:
    data, addr = sock.recvfrom(256)
    print(f'Received {len(data)} bytes from {addr}: {data.hex()}')
    if len(data) >= 7:
        magic, ver, msg_type, pad = data[0], data[1], data[2], data[3]
        port = struct.unpack('<H', data[4:6])[0]
        name_len = data[6]
        name = data[7:7+name_len].decode('utf-8', errors='replace')
        print(f'Parsed: Magic={hex(magic)}, Ver={ver}, Type={msg_type}, Pad={pad}, Port={port}, PC Name="{name}"')
        sys.exit(0)
except Exception as e:
    print('No response (server might not be running yet):', e)
    sys.exit(1)
