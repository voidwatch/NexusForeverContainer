using System;
using NexusForever.Database.Character.Model;
using NexusForever.WorldServer.Game.Entity.Static;
using NexusForever.WorldServer.Game.Reputation.Static;

namespace NexusForever.WorldServer.Game.CharacterCache
{
    public class CharacterInfo : ICharacter
    {
        public ulong CharacterId { get; }
        public string Name { get; }
        public Sex Sex { get; }
        public Race Race { get;}
        public Class Class { get; }
        public Path Path { get; }
        public uint Level { get; }
        public Faction Faction1 { get; }
        public Faction Faction2 { get; }
        public DateTime? LastOnline { get; }

        public CharacterInfo(CharacterModel model)
        {
            CharacterId = model.Id;
            Name        = model.Name;
            Sex         = (Sex)model.Sex;
            Race        = (Race)model.Race;
            Class       = (Class)model.Class;
            Path        = (Path)model.ActivePath;
            Level       = model.Level;
            Faction1    = (Faction)model.FactionId;
            Faction2    = (Faction)model.FactionId;
            LastOnline  = model.LastOnline;
        }

        public CharacterInfo(ICharacter model)
        {
            CharacterId = model.CharacterId;
            Name        = model.Name;
            Sex         = model.Sex;
            Race        = model.Race;
            Class       = model.Class;
            Path        = model.Path;
            Level       = model.Level;
            Faction1    = model.Faction1;
            Faction2    = model.Faction1;
        }

        /// <summary>
        /// Returns a <see cref="float"/> representing decimal value, in days, since the character was last online.
        /// </summary>
        public float? GetOnlineStatus()
        {
            if (!LastOnline.HasValue)
                return null;

            return (float)DateTime.UtcNow.Subtract(LastOnline.Value).TotalDays * -1f;
        }
    }
}
