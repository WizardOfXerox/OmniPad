using System.Net;
using OmniPadServer.Core;
using Xunit;

namespace OmniPadServer.Tests;

public class SessionManagerTests
{
    [Fact]
    public void AssignSlot_AssignsSequentialSlotsAndRejectsWhenFull()
    {
        var manager = new SessionManager();
        for (int i = 0; i < IPadBackend.MaxPads; i++)
        {
            var ep = new IPEndPoint(IPAddress.Parse($"192.168.1.{i + 10}"), 1000);
            Assert.Equal(i, manager.AssignSlot(ep));
        }

        // 17th phone is rejected (MaxPads limit)
        var epExtra = new IPEndPoint(IPAddress.Parse("192.168.1.99"), 1000);
        Assert.Equal(Protocol.NoPad, manager.AssignSlot(epExtra));
    }

    [Fact]
    public void SequenceWrap_HandlesBoundaryCorrectly()
    {
        var manager = new SessionManager();
        var ep = new IPEndPoint(IPAddress.Parse("192.168.1.10"), 1000);
        byte slot = manager.AssignSlot(ep);

        // Advance to near max uint32
        uint nearMax = uint.MaxValue - 2;
        Assert.True(manager.TryProcessInput(ep, slot, nearMax, out _));

        // Advance to max
        Assert.True(manager.TryProcessInput(ep, slot, uint.MaxValue, out _));

        // Wrap around to 0
        Assert.True(manager.TryProcessInput(ep, slot, 0, out _));

        // Wrap around to 1
        Assert.True(manager.TryProcessInput(ep, slot, 1, out _));

        // Stale packet (0 is older than 1) -> must be rejected
        Assert.False(manager.TryProcessInput(ep, slot, 0, out _));
    }

    [Fact]
    public void AssignSlot_SameIpNewPort_ReclaimsExistingSlot()
    {
        var manager = new SessionManager();
        var ip = IPAddress.Parse("192.168.1.55");
        var epInitial = new IPEndPoint(ip, 50001);
        var epReloaded = new IPEndPoint(ip, 50002);

        // Initial phone connection gets Slot 0 (P1)
        byte slot0 = manager.AssignSlot(epInitial);
        Assert.Equal(0, slot0);

        // Browser refreshes and reconnects with new ephemeral port -> must reclaim Slot 0 (P1)
        byte slotReload = manager.AssignSlot(epReloaded);
        Assert.Equal(0, slotReload);

        // Disconnecting the OLD socket endpoint should return false because it was already replaced
        Assert.False(manager.Disconnect(epInitial, slot0));

        // Active connection on the new endpoint can still submit input
        Assert.True(manager.TryProcessInput(epReloaded, 0, 1, out byte processedSlot));
        Assert.Equal(0, processedSlot);
    }

    [Fact]
    public void GetSlotStatuses_ReturnsCorrectOccupancy()
    {
        var manager = new SessionManager();
        var ep0 = new IPEndPoint(IPAddress.Parse("192.168.1.10"), 1000);

        manager.AssignSlot(ep0); // Slot 0
        var statuses = manager.GetSlotStatuses();
        Assert.Equal(IPadBackend.MaxPads, statuses.Length);
        Assert.Equal(1, statuses[0]);
        for (int i = 1; i < statuses.Length; i++) Assert.Equal(0, statuses[i]);

        // Move to empty slot 2
        Assert.True(manager.MoveToEmptySlot(0, 2));
        var updated = manager.GetSlotStatuses();
        Assert.Equal(0, updated[0]);
        Assert.Equal(1, updated[2]);
    }

    [Fact]
    public void MoveToEmptySlot_TransfersSessionCorrectly()
    {
        var manager = new SessionManager();
        var ep = new IPEndPoint(IPAddress.Parse("192.168.1.10"), 1000);
        byte slot = manager.AssignSlot(ep); // Slot 0
        Assert.Equal(0, slot);

        // Cannot move to same slot or out of bounds
        Assert.False(manager.MoveToEmptySlot(0, 0));
        Assert.False(manager.MoveToEmptySlot(0, (byte)IPadBackend.MaxPads));

        // Move from 0 to 2
        Assert.True(manager.MoveToEmptySlot(0, 2));
        Assert.Null(manager.GetSession(0));
        Assert.NotNull(manager.GetSession(2));
        Assert.Equal(2, manager.GetSession(2)!.Slot);

        // TryProcessInput now maps ep to slot 2
        Assert.True(manager.TryProcessInput(ep, 2, 1, out byte newSlot));
        Assert.Equal(2, newSlot);
    }

    [Fact]
    public void SwapSlots_AtomicallySwapsOccupiedSlots()
    {
        var manager = new SessionManager();
        var epA = new IPEndPoint(IPAddress.Parse("192.168.1.10"), 1000);
        var epB = new IPEndPoint(IPAddress.Parse("192.168.1.20"), 2000);

        byte slotA = manager.AssignSlot(epA); // Slot 0 (P1)
        byte slotB = manager.AssignSlot(epB); // Slot 1 (P2)
        Assert.Equal(0, slotA);
        Assert.Equal(1, slotB);

        // Swap P1 and P2
        Assert.True(manager.SwapSlots(0, 1));

        // Now epA is Slot 1, epB is Slot 0
        Assert.Equal(1, manager.GetSession(1)!.Slot);
        Assert.Equal(epA, manager.GetSession(1)!.EndPoint);

        Assert.Equal(0, manager.GetSession(0)!.Slot);
        Assert.Equal(epB, manager.GetSession(0)!.EndPoint);

        // Input verification
        Assert.True(manager.TryProcessInput(epA, 1, 1, out byte routedSlotA));
        Assert.Equal(1, routedSlotA);

        Assert.True(manager.TryProcessInput(epB, 0, 1, out byte routedSlotB));
        Assert.Equal(0, routedSlotB);

        // Swapping with an empty slot should fail (must use MoveToEmptySlot)
        Assert.False(manager.SwapSlots(0, 3));
    }

    [Fact]
    public void AssignSlot_WithSessionId_ReclaimsExactSlotAcrossEphemeralPorts()
    {
        var manager = new SessionManager();
        var ep1 = new IPEndPoint(IPAddress.Parse("192.168.1.10"), 5001);
        var ep2 = new IPEndPoint(IPAddress.Parse("192.168.1.20"), 5002);
        var ep3 = new IPEndPoint(IPAddress.Parse("192.168.1.30"), 5003);

        byte s1 = manager.AssignSlot(ep1, isPersistent: true, sessionId: "tab_1");
        byte s2 = manager.AssignSlot(ep2, isPersistent: true, sessionId: "tab_2");
        byte s3 = manager.AssignSlot(ep3, isPersistent: true, sessionId: "tab_3");

        Assert.Equal(0, s1);
        Assert.Equal(1, s2);
        Assert.Equal(2, s3);

        // Player 2 refreshes their browser tab (new ephemeral port, same tab_2 sessionId)
        var ep2Reloaded = new IPEndPoint(IPAddress.Parse("192.168.1.20"), 5009);
        byte s2Reloaded = manager.AssignSlot(ep2Reloaded, isPersistent: true, sessionId: "tab_2");

        // Must reclaim Player 2 (Slot 1) and NOT jump to Player 4 (Slot 3)
        Assert.Equal(1, s2Reloaded);
        Assert.Equal(ep2Reloaded, manager.GetSession(1)!.EndPoint);
    }

    [Fact]
    public void AssignSlot_MultipleLoopbackTabs_DistinctSessionIds_AssignedSeparateSlots()
    {
        var manager = new SessionManager();
        var ep1 = new IPEndPoint(IPAddress.Loopback, 5001);
        var ep2 = new IPEndPoint(IPAddress.Loopback, 5002);
        var ep3 = new IPEndPoint(IPAddress.Loopback, 5003);

        byte s1 = manager.AssignSlot(ep1, isPersistent: true, sessionId: "local_tab_1");
        byte s2 = manager.AssignSlot(ep2, isPersistent: true, sessionId: "local_tab_2");
        byte s3 = manager.AssignSlot(ep3, isPersistent: true, sessionId: "local_tab_3");

        Assert.Equal(0, s1);
        Assert.Equal(1, s2);
        Assert.Equal(2, s3);

        // Tab 2 refreshes on loopback
        var ep2Reloaded = new IPEndPoint(IPAddress.Loopback, 5099);
        byte s2Reloaded = manager.AssignSlot(ep2Reloaded, isPersistent: true, sessionId: "local_tab_2");
        Assert.Equal(1, s2Reloaded);
    }

    [Fact]
    public void CheckTimeouts_ReapsPersistentWebSocketSessionsWhenInactive()
    {
        // Session manager with 50 millisecond timeout
        var manager = new SessionManager(timeoutSeconds: 0.05);
        var ep = new IPEndPoint(IPAddress.Parse("192.168.1.50"), 8000);

        byte slot = manager.AssignSlot(ep, isPersistent: true, sessionId: "ws_client");
        Assert.Equal(0, slot);
        Assert.Equal(1, manager.GetSlotStatuses()[0]);

        // Wait for timeout to expire
        Thread.Sleep(80);
        manager.CheckTimeouts();

        // Slot 0 must be reaped and freed
        Assert.Equal(0, manager.GetSlotStatuses()[0]);
        Assert.Null(manager.GetSession(0));
    }

    [Fact]
    public void DisconnectBySlot_WithDifferentSessionId_DoesNotDisconnectReclaimedSlot()
    {
        var manager = new SessionManager();
        var ep = new IPEndPoint(IPAddress.Parse("192.168.1.50"), 8000);

        manager.AssignSlot(ep, isPersistent: true, sessionId: "active_session");

        // Old stale disconnect attempt with wrong sessionId must fail
        Assert.False(manager.DisconnectBySlot(0, expectedSessionId: "stale_session"));
        Assert.NotNull(manager.GetSession(0));

        // Matching sessionId must succeed
        Assert.True(manager.DisconnectBySlot(0, expectedSessionId: "active_session"));
        Assert.Null(manager.GetSession(0));
    }

    [Fact]
    public void DisconnectBySessionId_FreesSlotImmediately()
    {
        var manager = new SessionManager();
        var ep = new IPEndPoint(IPAddress.Parse("192.168.1.50"), 8000);

        manager.AssignSlot(ep, isPersistent: true, sessionId: "beacon_session");
        Assert.Equal(1, manager.GetSlotStatuses()[0]);

        Assert.True(manager.DisconnectBySessionId("beacon_session"));
        Assert.Equal(0, manager.GetSlotStatuses()[0]);
    }
}
