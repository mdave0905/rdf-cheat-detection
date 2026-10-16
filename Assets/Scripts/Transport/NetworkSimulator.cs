using System;
using System.Collections.Generic;
using System.Net;

/// <summary>
/// Emulates one direction of a network link on top of a real UDP socket.
/// Loss and delay are applied exactly once, on the sending side:
///   - each outgoing datagram is dropped with probability PacketLossProbability;
///   - surviving datagrams carry the simulation tick at which they may be delivered
///     (send tick + one-way latency rounded up to whole ticks).
/// The receiver holds datagrams until that tick. Delay is fixed (no jitter), and
/// datagrams are never reordered or retransmitted.
/// </summary>
public class NetworkSimulator
{
    private const int HeaderBytes = 8;

    private readonly UDPSocket socket;
    private readonly Random random;
    private readonly List<PendingPacket> receiveQueue = new List<PendingPacket>();

    public float LatencySeconds { get; set; }
    public float PacketLossProbability { get; set; }

    public int DatagramsSent { get; private set; }
    public int DatagramsDropped { get; private set; }
    public int DatagramsDelivered { get; private set; }

    /// <summary>
    /// Datagrams that reached this socket after their due tick because the operating
    /// system had not delivered them in time. Must stay 0 for a deterministic session.
    /// </summary>
    public int LateDeliveries { get; private set; }

    public uint DelayTicks => SimulationClock.DelayToTicks(LatencySeconds);

    private struct PendingPacket
    {
        public uint dueTick;
        public byte[] payload;
        public EndPoint sender;
    }

    public NetworkSimulator(int seed)
    {
        socket = new UDPSocket();
        random = new Random(seed);
    }

    public void Bind(string ipAddress, int port)
    {
        socket.Bind(ipAddress, port);
    }

    public void SendTo(byte[] payload, string remoteIp, int remotePort)
    {
        byte[] datagram = PrepareDatagram(payload);
        if (datagram != null) socket.SendTo(datagram, remoteIp, remotePort);
    }

    public void SendTo(byte[] payload, EndPoint endPoint)
    {
        byte[] datagram = PrepareDatagram(payload);
        if (datagram != null) socket.SendTo(datagram, endPoint);
    }

    private byte[] PrepareDatagram(byte[] payload)
    {
        DatagramsSent++;

        // Always draw, so the loss pattern depends only on the seed and the
        // number of datagrams sent.
        double draw = random.NextDouble();
        if (draw < PacketLossProbability)
        {
            DatagramsDropped++;
            return null;
        }

        uint sendTick = SimulationClock.Tick;
        uint dueTick = sendTick + DelayTicks;
        byte[] datagram = new byte[HeaderBytes + payload.Length];
        BitConverter.GetBytes(sendTick).CopyTo(datagram, 0);
        BitConverter.GetBytes(dueTick).CopyTo(datagram, 4);
        payload.CopyTo(datagram, HeaderBytes);
        return datagram;
    }

    public bool TryReceive(out byte[] payload, out EndPoint senderEndPoint)
    {
        while (socket.TryReceive(out byte[] rawBuffer, out int rawBytesRead, out EndPoint rawSender))
        {
            if (rawBytesRead < HeaderBytes) continue;

            uint sendTick = BitConverter.ToUInt32(rawBuffer, 0);
            uint dueTick = BitConverter.ToUInt32(rawBuffer, 4);

            // With zero delay a datagram may legitimately be read one tick after it
            // was sent (the receiver had already run in that tick).
            uint latestOnTimeTick = Math.Max(dueTick, sendTick + 1);
            if (SimulationClock.Tick > latestOnTimeTick) LateDeliveries++;

            byte[] body = new byte[rawBytesRead - HeaderBytes];
            Buffer.BlockCopy(rawBuffer, HeaderBytes, body, 0, body.Length);
            receiveQueue.Add(new PendingPacket
            {
                dueTick = dueTick,
                payload = body,
                sender = rawSender
            });
        }

        // A fixed delay per link means datagrams become due in the order they were sent.
        if (receiveQueue.Count > 0 && SimulationClock.Tick >= receiveQueue[0].dueTick)
        {
            PendingPacket ready = receiveQueue[0];
            receiveQueue.RemoveAt(0);
            DatagramsDelivered++;

            payload = ready.payload;
            senderEndPoint = ready.sender;
            return true;
        }

        payload = null;
        senderEndPoint = null;
        return false;
    }

    public void Close()
    {
        socket.Close();
        receiveQueue.Clear();
    }
}
