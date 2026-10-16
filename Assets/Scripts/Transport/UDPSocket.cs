using System;
using System.Net;
using System.Net.Sockets;

public class UDPSocket
{
    private Socket socket;

    public void Bind(string ipAddress, int port)
    {
        socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Blocking = false;

        // On Windows, an ICMP "port unreachable" makes the next ReceiveFrom throw
        // ConnectionReset. Disable that behaviour so a closed peer cannot stall reads.
        try
        {
            const int SIO_UDP_CONNRESET = -1744830452;
            socket.IOControl(SIO_UDP_CONNRESET, new byte[] { 0 }, null);
        }
        catch (Exception)
        {
            // Not supported on this platform; nothing to disable.
        }

        IPEndPoint localEndPoint = new IPEndPoint(IPAddress.Parse(ipAddress), port);
        socket.Bind(localEndPoint);
    }

    public void SendTo(byte[] data, string remoteIp, int remotePort)
    {
        if (socket == null) return;
        IPEndPoint endPoint = new IPEndPoint(IPAddress.Parse(remoteIp), remotePort);
        socket.SendTo(data, endPoint);
    }

    public void SendTo(byte[] data, EndPoint endPoint)
    {
        if (socket == null) return;
        socket.SendTo(data, endPoint);
    }

    public bool TryReceive(out byte[] buffer, out int bytesRead, out EndPoint senderEndPoint)
    {
        buffer = new byte[1024];
        bytesRead = 0;
        senderEndPoint = new IPEndPoint(IPAddress.Any, 0);

        if (socket == null || socket.Available == 0)
        {
            return false;
        }

        try
        {
            bytesRead = socket.ReceiveFrom(buffer, ref senderEndPoint);
            return bytesRead > 0;
        }
        catch (SocketException)
        {
            return false;
        }
    }


    public void Close()
    {
        if (socket != null)
        {
            socket.Close();
            socket = null;
        }
    }
}
