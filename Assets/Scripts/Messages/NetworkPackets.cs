using System.IO;
using UnityEngine;

public struct InputPacket
{
    public uint sequenceNumber;
    public float timestamp;
    public float inputX;
    public float inputY;
    public bool sprinting;
    public Vector3 claimedPosition;

    public byte[] Serialize()
    {
        using (MemoryStream stream = new MemoryStream())
        {
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(sequenceNumber);
                writer.Write(timestamp);
                writer.Write(inputX);
                writer.Write(inputY);
                writer.Write(sprinting);
                writer.Write(claimedPosition.x);
                writer.Write(claimedPosition.y);
                writer.Write(claimedPosition.z);
                return stream.ToArray();
            }
        }
    }

    public static InputPacket Deserialize(byte[] data)
    {
        InputPacket packet = new InputPacket();
        using (MemoryStream stream = new MemoryStream(data))
        {
            using (BinaryReader reader = new BinaryReader(stream))
            {
                packet.sequenceNumber = reader.ReadUInt32();
                packet.timestamp = reader.ReadSingle();
                packet.inputX = reader.ReadSingle();
                packet.inputY = reader.ReadSingle();
                packet.sprinting = reader.ReadBoolean();
                float x = reader.ReadSingle();
                float y = reader.ReadSingle();
                float z = reader.ReadSingle();
                packet.claimedPosition = new Vector3(x, y, z);
            }
        }
        return packet;
    }
}

public struct StatePacket
{
    public uint ackSequenceNumber;
    public float serverTimestamp;
    public Vector3 authoritativePosition;

    public byte[] Serialize()
    {
        using (MemoryStream stream = new MemoryStream())
        {
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(ackSequenceNumber);
                writer.Write(serverTimestamp);
                writer.Write(authoritativePosition.x);
                writer.Write(authoritativePosition.y);
                writer.Write(authoritativePosition.z);
                return stream.ToArray();
            }
        }
    }

    public static StatePacket Deserialize(byte[] data)
    {
        StatePacket packet = new StatePacket();
        using (MemoryStream stream = new MemoryStream(data))
        {
            using (BinaryReader reader = new BinaryReader(stream))
            {
                packet.ackSequenceNumber = reader.ReadUInt32();
                packet.serverTimestamp = reader.ReadSingle();
                float x = reader.ReadSingle();
                float y = reader.ReadSingle();
                float z = reader.ReadSingle();
                packet.authoritativePosition = new Vector3(x, y, z);
            }
        }
        return packet;
    }
}
