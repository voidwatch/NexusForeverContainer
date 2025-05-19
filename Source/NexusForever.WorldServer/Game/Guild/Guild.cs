﻿using System;
using System.Linq;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using NexusForever.Database.Character;
using NexusForever.Database.Character.Model;
using NexusForever.WorldServer.Game.Achievement;
using NexusForever.WorldServer.Game.Guild.Static;
using NexusForever.WorldServer.Game.Social.Static;
using NexusForever.WorldServer.Network.Message.Model.Shared;

namespace NexusForever.WorldServer.Game.Guild
{
    public partial class Guild : GuildChat
    {
        public override uint MaxMembers => 40u;

        public GuildStandard Standard { get; }
        public GuildAchievementManager AchievementManager { get; }

        public string MessageOfTheDay
        {
            get => messageOfTheDay;
            set
            {
                messageOfTheDay = value;
                saveMask |= GuildSaveMask.MessageOfTheDay;
            }
        }
        private string messageOfTheDay;

        public string AdditionalInfo
        {
            get => additionalInfo;
            set
            {
                additionalInfo = value;
                saveMask |= GuildSaveMask.AdditionalInfo;
            }
        }
        private string additionalInfo;

        private GuildSaveMask saveMask;

        /// <summary>
        /// Create a new <see cref="Guild"/> from an existing database model.
        /// </summary>
        public Guild(GuildModel model) 
            : base(model)
        {
            Standard           = new GuildStandard(model.GuildData);
            AchievementManager = new GuildAchievementManager(this, model);
            messageOfTheDay    = model.GuildData.MessageOfTheDay;
            additionalInfo     = model.GuildData.AdditionalInfo;

            InitialiseChatChannels(ChatChannelType.Guild, ChatChannelType.GuildOfficer);
        }

        /// <summary>
        /// Create a new <see cref="Guild"/> using the supplied parameters.
        /// </summary>
        public Guild(string name, string leaderRankName, string councilRankName, string memberRankName, GuildStandard standard)
            : base(GuildType.Guild, name, leaderRankName, councilRankName, memberRankName)
        {
            Standard           = standard;
            AchievementManager = new GuildAchievementManager(this);
            messageOfTheDay    = "";
            additionalInfo     = "";

            InitialiseChatChannels(ChatChannelType.Guild, ChatChannelType.GuildOfficer);
        }

        protected override void Save(CharacterContext context, GuildBaseSaveMask baseSaveMask)
        {
            if ((baseSaveMask & GuildBaseSaveMask.Create) != 0)
            {
                context.Add(new GuildDataModel
                {
                    Id                   = Id,
                    AdditionalInfo       = AdditionalInfo,
                    MessageOfTheDay      = MessageOfTheDay,
                    BackgroundIconPartId = (ushort)Standard.BackgroundIcon.GuildStandardPartEntry.Id,
                    ForegroundIconPartId = (ushort)Standard.ForegroundIcon.GuildStandardPartEntry.Id,
                    ScanLinesPartId      = (ushort)Standard.ScanLines.GuildStandardPartEntry.Id
                });
            }

            if (saveMask != GuildSaveMask.None)
            {
                var model = new GuildDataModel
                {
                    Id = Id
                };

                EntityEntry<GuildDataModel> entity = context.Attach(model);
                if ((saveMask & GuildSaveMask.MessageOfTheDay) != 0)
                {
                    model.MessageOfTheDay = MessageOfTheDay;
                    entity.Property(p => p.MessageOfTheDay).IsModified = true;
                }

                if ((saveMask & GuildSaveMask.AdditionalInfo) != 0)
                {
                    model.AdditionalInfo = AdditionalInfo;
                    entity.Property(p => p.AdditionalInfo).IsModified = true;
                }

                saveMask = GuildSaveMask.None;
            }

            AchievementManager.Save(context);
        }

        public override GuildData Build()
        {
            return new GuildData
            {
                GuildId           = Id,
                GuildName         = Name,
                Flags             = Flags,
                Type              = Type,
                Ranks             = GetGuildRanksPackets().ToList(),
                GuildStandard     = Standard.Build(),
                MemberCount       = (uint)members.Count,
                OnlineMemberCount = (uint)onlineMembers.Count,
                GuildInfo =
                {
                    MessageOfTheDay         = MessageOfTheDay,
                    GuildInfo               = AdditionalInfo,
                    GuildCreationDateInDays = (float)DateTime.Now.Subtract(CreateTime).TotalDays * -1f
                }
            };
        }

        /// <summary>
        /// Set if taxes are enabled for <see cref="Guild"/>.
        /// </summary>
        public void SetTaxes(bool enabled)
        {
            if (enabled)
                SetFlag(GuildFlag.Taxes);
            else
                RemoveFlag(GuildFlag.Taxes);

            SendGuildFlagUpdate();
        }
    }
}
