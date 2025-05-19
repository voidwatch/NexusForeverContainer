using NexusForever.Shared.Network;

namespace NexusForever.WorldServer.Game.Entity.Network.Command
{
    [EntityCommand(EntityCommand.SetVelocity)]
    public class SetVelocityCommand : IEntityCommandModel
    {
        public Velocity VelocityData { get; set; } = new Velocity();
        public bool Blend { get; set; }

        public void Read(GamePacketReader reader)
        {
            VelocityData.Read(reader);
            Blend = reader.ReadBit();
        }

        public void Write(GamePacketWriter writer)
        {
            VelocityData.Write(writer);
            writer.Write(Blend);
        }
    }
}
