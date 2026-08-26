using RepoSteamNetworking.API;
using RepoSteamNetworking.Networking.Serialization;
using UnityEngine;

namespace ElsaPetMod
{
    public class PetStatePacket : NetworkPacket<PetStatePacket>
    {
        public int PetViewID;
        public int OwnerViewID;
        public int StateIndex;
        public int Sequence;
        public Vector3 Position;
        public Quaternion Rotation;

        protected override void WriteData(SocketMessage socketMessage)
        {
            socketMessage.Write(PetViewID);
            socketMessage.Write(OwnerViewID);
            socketMessage.Write(StateIndex);
            socketMessage.Write(Sequence);
            socketMessage.Write(Position);
            socketMessage.Write(Rotation);
        }

        protected override void ReadData(SocketMessage socketMessage)
        {
            PetViewID = socketMessage.Read<int>();
            OwnerViewID = socketMessage.Read<int>();
            StateIndex = socketMessage.Read<int>();
            Sequence = socketMessage.Read<int>();
            Position = socketMessage.Read<Vector3>();
            Rotation = socketMessage.Read<Quaternion>();
        }
    }

    public class PetGiveItemPacket : NetworkPacket<PetGiveItemPacket>
    {
        public int PetViewID;
        public int ItemViewID;

        protected override void WriteData(SocketMessage socketMessage)
        {
            socketMessage.Write(PetViewID);
            socketMessage.Write(ItemViewID);
        }

        protected override void ReadData(SocketMessage socketMessage)
        {
            PetViewID = socketMessage.Read<int>();
            ItemViewID = socketMessage.Read<int>();
        }
    }

    public class PetSyncPettingPacket : NetworkPacket<PetSyncPettingPacket>
    {
        public int PetViewID;

        protected override void WriteData(SocketMessage socketMessage)
        {
            socketMessage.Write(PetViewID);
        }

        protected override void ReadData(SocketMessage socketMessage)
        {
            PetViewID = socketMessage.Read<int>();
        }
    }

    public class PetCarryPlayerPacket : NetworkPacket<PetCarryPlayerPacket>
    {
        public int PetViewID;
        public int PlayerViewID;

        protected override void WriteData(SocketMessage socketMessage)
        {
            socketMessage.Write(PetViewID);
            socketMessage.Write(PlayerViewID);
        }

        protected override void ReadData(SocketMessage socketMessage)
        {
            PetViewID = socketMessage.Read<int>();
            PlayerViewID = socketMessage.Read<int>();
        }
    }

    public class PetSyncCarryPacket : NetworkPacket<PetSyncCarryPacket>
    {
        public int PetViewID;
        public int TargetViewID;
        public bool IsPlayer;
        public bool IsPickingUp;
        public bool InheritScale; // Adicionado para sincronizar a decisão do Master

        protected override void WriteData(SocketMessage socketMessage)
        {
            socketMessage.Write(PetViewID);
            socketMessage.Write(TargetViewID);
            socketMessage.Write(IsPlayer);
            socketMessage.Write(IsPickingUp);
            socketMessage.Write(InheritScale);
        }

        protected override void ReadData(SocketMessage socketMessage)
        {
            PetViewID = socketMessage.Read<int>();
            TargetViewID = socketMessage.Read<int>();
            IsPlayer = socketMessage.Read<bool>();
            IsPickingUp = socketMessage.Read<bool>();
            InheritScale = socketMessage.Read<bool>();
        }
    }

    public class PetSwitchOwnerPacket : NetworkPacket<PetSwitchOwnerPacket>
    {
        protected override void WriteData(SocketMessage socketMessage)
        {
        }

        protected override void ReadData(SocketMessage socketMessage)
        {
        }
    }

    public class PetSpawnPacket : NetworkPacket<PetSpawnPacket>
    {
        public Vector3 Position;
        public string ContextName;
        public int AllocatedViewID;

        protected override void WriteData(SocketMessage socketMessage)
        {
            socketMessage.Write(Position);
            socketMessage.Write(ContextName);
            socketMessage.Write(AllocatedViewID);
        }

        protected override void ReadData(SocketMessage socketMessage)
        {
            Position = socketMessage.Read<Vector3>();
            ContextName = socketMessage.Read<string>();
            AllocatedViewID = socketMessage.Read<int>();
        }
    }

    public class PetExplodePacket : NetworkPacket<PetExplodePacket>
    {
        public int PetViewID;
        public float Delay;

        protected override void WriteData(SocketMessage socketMessage)
        {
            socketMessage.Write(PetViewID);
            socketMessage.Write(Delay);
        }

        protected override void ReadData(SocketMessage socketMessage)
        {
            PetViewID = socketMessage.Read<int>();
            Delay = socketMessage.Read<float>();
        }
    }
    public class PetRequestSyncPacket : NetworkPacket<PetRequestSyncPacket>
    {
        public ulong SenderSteamID;

        protected override void WriteData(SocketMessage socketMessage)
        {
            socketMessage.Write(SenderSteamID);
        }

        protected override void ReadData(SocketMessage socketMessage)
        {
            SenderSteamID = socketMessage.Read<ulong>();
        }
    }
}