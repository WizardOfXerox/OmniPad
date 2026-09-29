import socket
import struct
import time
import sys

MAGIC = 0xDA
VERSION = 1
MSG_DISCOVER = 0x06
MSG_WELCOME = 0x03
NO_PAD = 0xFF

sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
try:
    sock.bind(('0.0.0.0', 27501))
except Exception as e:
    print(f"Error binding: {e}")
    sys.exit(1)

pc_name = b"LIVINGROOM-GAMING"
resp = bytearray([MAGIC, VERSION, MSG_WELCOME, NO_PAD])
resp += struct.pack('<H', 27502)
resp.append(len(pc_name))
resp += pc_name

print("Mock Second PC Discovery Server running on 0.0.0.0:27501...")
sys.stdout.flush()

while True:
    try:
        data, addr = sock.recvfrom(256)
        if len(data) >= 4 and data[0] == MAGIC and data[2] == MSG_DISCOVER:
            print(f"Received discover from {addr}, replying as LIVINGROOM-GAMING...")
            sock.sendto(resp, addr)
            sys.stdout.flush()
    except Exception as e:
        print(f"Error: {e}")
        break
