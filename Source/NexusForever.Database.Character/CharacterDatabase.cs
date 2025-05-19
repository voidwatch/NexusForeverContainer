﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NexusForever.Database.Character.Model;
using NexusForever.Database.Configuration;
using NLog;

namespace NexusForever.Database.Character
{
    public class CharacterDatabase
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private readonly IDatabaseConfig config;

        public CharacterDatabase(IDatabaseConfig config)
        {
            this.config = config;
        }

        public async Task Save(Action<CharacterContext> action)
        {
            await using var context = new CharacterContext(config);
            action.Invoke(context);
            await context.SaveChangesAsync();
        }

        public async Task Save(ISaveCharacter entity)
        {
            await using var context = new CharacterContext(config);
            entity.Save(context);
            await context.SaveChangesAsync();
        }

        public async Task Save(IEnumerable<ISaveCharacter> entities)
        {
            await using var context = new CharacterContext(config);
            foreach (ISaveCharacter entity in entities)
                entity.Save(context);
            await context.SaveChangesAsync();
        }

        public void Migrate()
        {
            using var context = new CharacterContext(config);

            List<string> migrations = context.Database.GetPendingMigrations().ToList();
            if (migrations.Count > 0)
            {
                log.Info($"Applying {migrations.Count} authentication database migration(s)...");
                foreach (string migration in migrations)
                    log.Info(migration);

                context.Database.Migrate();
            }
        }

        public List<CharacterModel> GetAllCharacters()
        {
            using var context = new CharacterContext(config);
            return context.Character.Where(c => c.DeleteTime == null).ToList();
        }

        public ulong GetNextCharacterId()
        {
            using var context = new CharacterContext(config);
            return context.Character
                .Select(r => r.Id)
                .DefaultIfEmpty()
                .Max();
        }

        public async Task<CharacterModel> GetCharacterById(ulong characterId)
        {
            await using var context = new CharacterContext(config);
            return await context.Character.FirstOrDefaultAsync(e => e.Id == characterId);
        }

        public async Task<CharacterModel> GetCharacterByName(string name)
        {
            await using var context = new CharacterContext(config);
            return await context.Character.FirstOrDefaultAsync(e => e.Name == name);
        }

        public ulong GetNextItemId()
        {
            using var context = new CharacterContext(config);
            return context.Item
                .Select(r => r.Id)
                .DefaultIfEmpty()
                .Max();
        }

        public ulong GetNextResidenceId()
        {
            using var context = new CharacterContext(config);
            return context.Residence
                .Select(r => r.Id)
                .DefaultIfEmpty()
                .Max();
        }

        public ulong GetNextDecorId()
        {
            using var context = new CharacterContext(config);
            return context.ResidenceDecor
                .Select(r => r.DecorId)
                .DefaultIfEmpty()
                .Max();
        }

        public async Task<List<CharacterModel>> GetCharacters(uint accountId)
        {
            await using var context = new CharacterContext(config);

            IQueryable<CharacterModel> query = context.Character.Where(c => c.AccountId == accountId);
            await query.SelectMany(c => c.Appearance).LoadAsync();
            await query.SelectMany(c => c.Customisation).LoadAsync();
            await query.SelectMany(c => c.Item).LoadAsync();
            await query.SelectMany(c => c.Bone).LoadAsync();
            await query.SelectMany(c => c.Currency).LoadAsync();
            await query.SelectMany(c => c.Path).LoadAsync();
            await query.SelectMany(c => c.CharacterTitle).LoadAsync();
            await query.SelectMany(c => c.Stat).LoadAsync();

            await query.SelectMany(c => c.Costume)
                .Include(c => c.CostumeItem)
                .LoadAsync();

            await query.SelectMany(c => c.PetCustomisation).LoadAsync();
            await query.SelectMany(c => c.PetFlair).LoadAsync();
            await query.SelectMany(c => c.Keybinding).LoadAsync();
            await query.SelectMany(c => c.Spell).LoadAsync();
            await query.SelectMany(c => c.ActionSetShortcut).LoadAsync();
            await query.SelectMany(c => c.ActionSetAmp).LoadAsync();
            await query.SelectMany(c => c.Datacube).LoadAsync();

            await query.SelectMany(c => c.Mail)
                .Include(c => c.Attachment)
                .ThenInclude(c => c.Item)
                .LoadAsync();

            await query.SelectMany(c => c.ZonemapHexgroup).LoadAsync();

            await query.SelectMany(c => c.Quest)
                .Include(c => c.QuestObjective)
                .LoadAsync();

            await query.SelectMany(c => c.Entitlement).LoadAsync();
            await query.SelectMany(c => c.Achievement).LoadAsync();

            await query.SelectMany(c => c.TradeskillMaterials).LoadAsync();

            await query.SelectMany(c => c.Reputation).LoadAsync();

            return await query.ToListAsync();
        }

        public bool CharacterNameExists(string characterName)
        {
            using var context = new CharacterContext(config);
            return context.Character.Any(c => c.Name == characterName);
        }

        public List<ResidenceModel> GetResidences()
        {
            using var context = new CharacterContext(config);
            return context.Residence
                .Include(r => r.Plot)
                .Include(r => r.Decor)
                .Include(r => r.Character)
                .Include(r => r.Guild)
                // only load residences where the owner character or guild hasn't been deleted
                .Where(r => (r.OwnerId.HasValue && !r.Character.DeleteTime.HasValue) || (r.GuildOwnerId.HasValue && !r.Guild.DeleteTime.HasValue))
                .ToList();
        }

        public ulong GetNextMailId()
        {
            using var context = new CharacterContext(config);
            return context.CharacterMail
                .Select(r => r.Id)
                .DefaultIfEmpty()
                .Max();
        }

        public ulong GetNextGuildId()
        {
            using var context = new CharacterContext(config);
            return context.Guild
                .Select(r => r.Id)
                .DefaultIfEmpty()
                .Max();
        }

        public List<GuildModel> GetGuilds()
        {
            using var context = new CharacterContext(config);
            return context.Guild
                .Where(g => g.DeleteTime == null)
                .Include(g => g.GuildRank)
                .Include(g => g.GuildMember)
                .Include(g => g.GuildData)
                .Include(g => g.Achievement)
                .ToList();
        }

        public List<ChatChannelModel> GetChatChannels()
        {
            using var context = new CharacterContext(config);
            return context.ChatChannel
                .Include(c => c.Members)
                .ToList();
        }

        public List<CharacterCreateModel> GetCharacterCreationData()
        {
            using var context = new CharacterContext(config);

            return context.CharacterCreate.ToList();
        }
    }
}
