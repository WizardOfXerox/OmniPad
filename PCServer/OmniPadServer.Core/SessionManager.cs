using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;

namespace OmniPadServer.Core;

public sealed class ClientSession
{
    public EndPoint EndPoint { get; set; }
    public byte Slot { get; set; }
    public string? SessionId { get; set; }
    public SequenceGate Gate { get; } = new();
    public long LastSeenTicks { get; set; }
    public ulong PacketsReceived { get; set; }
    public bool IsPersistent { get; set; }

    public ClientSession(EndPoint endPoint, byte slot, long ticks, bool isPersistent = false, string? sessionId = null)
    {
        EndPoint = endPoint;
        Slot = slot;
        LastSeenTicks = ticks;
        PacketsReceived = 0;
        IsPersistent = isPersistent;
        SessionId = sessionId;
    }
}

public sealed class SessionManager
{
    public const int MaxSlots = IPadBackend.MaxPads;
    private readonly object _lock = new();
    private readonly ClientSession?[] _slots = new ClientSession?[IPadBackend.MaxPads];
    private readonly Dictionary<EndPoint, byte> _endpointToSlot = new();
    private readonly long _timeoutTicks;

    public event Action<byte, EndPoint>? ClientConnected;
    public event Action<byte, EndPoint>? ClientDisconnected;

    public SessionManager(double timeoutSeconds = Protocol.SessionTimeoutSeconds)
    {
        _timeoutTicks = (long)(timeoutSeconds * Stopwatch.Frequency);
    }

    /// <summary>
    /// Handles a HELLO request or new client connection. Assigns a pad slot.
    /// Returns assigned slot (0-15), or Protocol.NoPad if full.
    /// </summary>
    public byte AssignSlot(EndPoint endPoint, bool isPersistent = false, string? sessionId = null)
    {
        lock (_lock)
        {
            long now = Stopwatch.GetTimestamp();

            // 1. If explicit sessionId provided, match existing session with the same sessionId
            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                for (byte i = 0; i < IPadBackend.MaxPads; i++)
                {
                    var existing = _slots[i];
                    if (existing != null && string.Equals(existing.SessionId, sessionId, StringComparison.Ordinal))
                    {
                        // Same client tab reconnecting (e.g. browser reload or network hop)
                        if (!existing.EndPoint.Equals(endPoint))
                        {
                            _endpointToSlot.Remove(existing.EndPoint);
                            existing.EndPoint = endPoint;
                            _endpointToSlot[endPoint] = i;
                        }
                        existing.LastSeenTicks = now;
                        existing.IsPersistent = existing.IsPersistent || isPersistent;
                        return i;
                    }
                }
            }

            // 2. If endpoint is already connected, refresh and return existing slot
            if (_endpointToSlot.TryGetValue(endPoint, out byte existingSlot))
            {
                var existing = _slots[existingSlot];
                if (existing != null)
                {
                    existing.LastSeenTicks = now;
                    existing.IsPersistent = existing.IsPersistent || isPersistent;
                    if (!string.IsNullOrWhiteSpace(sessionId))
                        existing.SessionId = sessionId;
                    return existingSlot;
                }
            }

            // 3. Fallback: If a client from the same remote IP address is reconnecting (without sessionId set),
            // seamlessly take over their existing slot so they retain their slot.
            if (string.IsNullOrWhiteSpace(sessionId) && endPoint is IPEndPoint newIpEp && !IPAddress.IsLoopback(newIpEp.Address))
            {
                for (byte i = 0; i < IPadBackend.MaxPads; i++)
                {
                    var slotSession = _slots[i];
                    if (slotSession?.EndPoint is IPEndPoint oldIpEp && 
                        oldIpEp.Address.Equals(newIpEp.Address) && 
                        string.IsNullOrWhiteSpace(slotSession.SessionId))
                    {
                        _endpointToSlot.Remove(oldIpEp);
                        _slots[i] = new ClientSession(endPoint, i, now, isPersistent, sessionId);
                        _endpointToSlot[endPoint] = i;
                        return i;
                    }
                }
            }

            // 4. Find lowest free slot
            for (byte i = 0; i < IPadBackend.MaxPads; i++)
            {
                if (_slots[i] == null)
                {
                    var session = new ClientSession(endPoint, i, now, isPersistent, sessionId);
                    _slots[i] = session;
                    _endpointToSlot[endPoint] = i;
                    ClientConnected?.Invoke(i, endPoint);
                    return i;
                }
            }

            return Protocol.NoPad; // Server is full
        }
    }

    /// <summary>
    /// Processes an incoming input packet. Verifies client, verifies slot, checks sequence wrap.
    /// Returns true if packet is newer and accepted; false if stale, duplicate, or unknown client.
    /// </summary>
    public bool TryProcessInput(EndPoint endPoint, byte slot, uint sequence, out byte assignedSlot)
    {
        assignedSlot = Protocol.NoPad;

        lock (_lock)
        {
            if (slot >= IPadBackend.MaxPads)
                return false;

            var session = _slots[slot];
            if (session == null || !session.EndPoint.Equals(endPoint))
                return false;

            if (!session.Gate.Accept(sequence))
                return false; // Stale or duplicate

            long now = Stopwatch.GetTimestamp();
            session.LastSeenTicks = now;
            session.PacketsReceived++;

            assignedSlot = slot;
            return true;
        }
    }

    /// <summary>
    /// Manually disconnects a client slot (e.g. on BYE message).
    /// </summary>
    public bool Disconnect(EndPoint endPoint, byte slot)
    {
        lock (_lock)
        {
            if (slot >= IPadBackend.MaxPads)
                return false;

            var session = _slots[slot];
            if (session != null && session.EndPoint.Equals(endPoint))
            {
                _slots[slot] = null;
                _endpointToSlot.Remove(endPoint);
                ClientDisconnected?.Invoke(slot, endPoint);
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Forcibly disconnects a slot regardless of endpoint (e.g. when its dedicated socket connection terminates).
    /// If expectedSessionId is provided, only disconnects if the slot still belongs to that sessionId.
    /// </summary>
    public bool DisconnectBySlot(byte slot, string? expectedSessionId = null)
    {
        lock (_lock)
        {
            if (slot >= IPadBackend.MaxPads)
                return false;

            var session = _slots[slot];
            if (session != null)
            {
                if (!string.IsNullOrWhiteSpace(expectedSessionId) &&
                    !string.Equals(session.SessionId, expectedSessionId, StringComparison.Ordinal))
                {
                    return false; // Slot reassigned to another session, do not disconnect!
                }

                var ep = session.EndPoint;
                _slots[slot] = null;
                _endpointToSlot.Remove(ep);
                ClientDisconnected?.Invoke(slot, ep);
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Forcibly disconnects a slot by its sessionId (e.g. on unload beacon or reload).
    /// </summary>
    public bool DisconnectBySessionId(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return false;

        lock (_lock)
        {
            for (byte i = 0; i < IPadBackend.MaxPads; i++)
            {
                var session = _slots[i];
                if (session != null && string.Equals(session.SessionId, sessionId, StringComparison.Ordinal))
                {
                    var ep = session.EndPoint;
                    _slots[i] = null;
                    _endpointToSlot.Remove(ep);
                    ClientDisconnected?.Invoke(i, ep);
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Refreshes the last seen timestamp of a connected slot (e.g. on WebSocket Ping/activity).
    /// </summary>
    public void TouchSession(byte slot)
    {
        lock (_lock)
        {
            if (slot < IPadBackend.MaxPads && _slots[slot] != null)
            {
                _slots[slot]!.LastSeenTicks = Stopwatch.GetTimestamp();
            }
        }
    }

    /// <summary>
    /// Finds any client session (UDP or WebSocket) that has exceeded the timeout threshold and disconnects them.
    /// </summary>
    public void CheckTimeouts()
    {
        long now = Stopwatch.GetTimestamp();

        lock (_lock)
        {
            for (byte i = 0; i < IPadBackend.MaxPads; i++)
            {
                var session = _slots[i];
                if (session != null && (now - session.LastSeenTicks) > _timeoutTicks)
                {
                    var ep = session.EndPoint;
                    _slots[i] = null;
                    _endpointToSlot.Remove(ep);
                    ClientDisconnected?.Invoke(i, ep);
                }
            }
        }
    }

    public EndPoint? GetClientEndPoint(int slot)
    {
        lock (_lock)
        {
            if (slot >= 0 && slot < IPadBackend.MaxPads)
                return _slots[slot]?.EndPoint;
            return null;
        }
    }

    public ClientSession? GetSession(byte slot)
    {
        lock (_lock)
        {
            if (slot < IPadBackend.MaxPads)
                return _slots[slot];
            return null;
        }
    }

    /// <summary>
    /// Returns an array of size 4 representing whether each slot 0..3 is occupied (1) or free (0).
    /// </summary>
    public byte[] GetSlotStatuses()
    {
        lock (_lock)
        {
            byte[] statuses = new byte[IPadBackend.MaxPads];
            for (byte i = 0; i < IPadBackend.MaxPads; i++)
            {
                statuses[i] = (byte)(_slots[i] != null ? 1 : 0);
            }
            return statuses;
        }
    }

    /// <summary>
    /// Moves an active session from one slot to an unoccupied slot.
    /// </summary>
    public bool MoveToEmptySlot(byte fromSlot, byte toSlot)
    {
        lock (_lock)
        {
            if (fromSlot >= IPadBackend.MaxPads || toSlot >= IPadBackend.MaxPads || fromSlot == toSlot)
                return false;

            var session = _slots[fromSlot];
            if (session == null || _slots[toSlot] != null)
                return false; // fromSlot empty or toSlot already occupied

            _slots[toSlot] = session;
            _slots[fromSlot] = null;
            session.Slot = toSlot;
            _endpointToSlot[session.EndPoint] = toSlot;
            return true;
        }
    }

    /// <summary>
    /// Atomically swaps the sessions of two occupied slots.
    /// </summary>
    public bool SwapSlots(byte slotA, byte slotB)
    {
        lock (_lock)
        {
            if (slotA >= IPadBackend.MaxPads || slotB >= IPadBackend.MaxPads || slotA == slotB)
                return false;

            var sessionA = _slots[slotA];
            var sessionB = _slots[slotB];

            if (sessionA == null || sessionB == null)
                return false; // Both must be active to swap

            _slots[slotA] = sessionB;
            _slots[slotB] = sessionA;

            sessionA.Slot = slotB;
            sessionB.Slot = slotA;

            _endpointToSlot[sessionA.EndPoint] = slotB;
            _endpointToSlot[sessionB.EndPoint] = slotA;

            return true;
        }
    }

    public int ConnectedCount
    {
        get
        {
            lock (_lock)
            {
                int count = 0;
                for (int i = 0; i < IPadBackend.MaxPads; i++)
                    if (_slots[i] != null) count++;
                return count;
            }
        }
    }
}
