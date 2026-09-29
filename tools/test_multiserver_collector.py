import socket
import struct
import threading
import time

MAGIC = 0xDA
VERSION = 1
MSG_DISCOVER = 0x06
MSG_WELCOME = 0x03
NO_PAD = 0xFF

# Mock secondary server response
def mock_second_server(port, stop_evt):
    s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    s.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    s.bind(('127.0.0.1', port))
    s.settimeout(0.5)
    
    # Pre-encode response for mock server
    pc_name = b"LIVINGROOM-RIG"
    resp = bytearray([MAGIC, VERSION, MSG_WELCOME, NO_PAD])
    resp += struct.pack('<H', 27502)
    resp.append(len(pc_name))
    resp += pc_name
    
    while not stop_evt.is_set():
        try:
            data, addr = s.recvfrom(256)
            if len(data) >= 4 and data[0] == MAGIC and data[2] == MSG_DISCOVER:
                s.sendto(resp, addr)
        except socket.timeout:
            continue
        except Exception:
            break
    s.close()

# Simulate Android discovery collector
def simulate_android_discovery():
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock.settimeout(0.2)
    
    discovery_payload = bytes([MAGIC, VERSION, MSG_DISCOVER, NO_PAD])
    # Query real server on 27501 and mock on 27505
    sock.sendto(discovery_payload, ('127.0.0.1', 27501))
    sock.sendto(discovery_payload, ('127.0.0.1', 27505))
    
    discovered = []
    seen = set()
    start = time.time()
    while time.time() - start < 1.0:
        try:
            buf, addr = sock.recvfrom(256)
            if len(buf) >= 4 and buf[0] == MAGIC and buf[2] == MSG_WELCOME:
                key = f"{addr[0]}:{addr[1]}"
                if key not in seen:
                    seen.add(key)
                    port = 27502
                    name = ""
                    if len(buf) >= 7:
                        port = struct.unpack('<H', buf[4:6])[0]
                        nlen = buf[6]
                        if len(buf) >= 7 + nlen and nlen > 0:
                            name = buf[7:7+nlen].decode('utf-8', errors='replace')
                    discovered.append({'addr': addr, 'name': name, 'port': port})
        except socket.timeout:
            continue
        except Exception:
            break
    sock.close()
    return discovered

stop_evt = threading.Event()
t = threading.Thread(target=mock_second_server, args=(27505, stop_evt))
t.daemon = True
t.start()
time.sleep(0.1)

servers = simulate_android_discovery()
stop_evt.set()
t.join()

print(f"Total discovered servers: {len(servers)}")
for idx, s in enumerate(servers):
    print(f"  [{idx+1}] PC: {s['name']} at {s['addr'][0]}:{s['addr'][1]} (Web Port: {s['port']})")

if len(servers) >= 2:
    print("[PASS] Multi-server discovery successfully collected all distinct servers without conflict!")
    exit(0)
else:
    print("[FAIL] Did not collect all servers")
    exit(1)
