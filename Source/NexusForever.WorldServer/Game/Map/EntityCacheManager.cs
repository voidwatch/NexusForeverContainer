﻿using System.Collections.Generic;
using NexusForever.Shared;
using NexusForever.Shared.Configuration;
using NexusForever.Shared.Database;
using NLog;

namespace NexusForever.WorldServer.Game.Map
{
    public sealed class EntityCacheManager : Singleton<EntityCacheManager>
    {
        private static readonly Logger log = LogManager.GetCurrentClassLogger();

        private readonly Dictionary<ushort, EntityCache> entityCaches = new();

        private EntityCacheManager()
        {
        }

        public void Initialise()
        {
            log.Info("Caching map spawns...");

            List<ushort> precachedBaseMaps = ConfigurationManager<WorldServerConfiguration>.Instance.Config.Map.PrecacheBaseMaps;
            if (precachedBaseMaps == null)
                return;

            foreach (ushort worldId in precachedBaseMaps)
                LoadEntityCache(worldId);
        }

        /// <summary>
        /// Returns an existing <see cref="EntityCache"/> for the supplied world, if it doesn't exist a new one will be created from the database.
        /// </summary>
        public EntityCache GetEntityCache(ushort worldId)
        {
            if (entityCaches.TryGetValue(worldId, out EntityCache entityCache))
                return entityCache;

            return LoadEntityCache(worldId);
        }

        private EntityCache LoadEntityCache(ushort worldId)
        {
            var entityCache = new EntityCache(DatabaseManager.Instance.WorldDatabase.GetEntities(worldId));
            entityCaches.Add(worldId, entityCache);

            log.Trace($"Initialised {entityCache.EntityCount} spawns on {entityCache.GridCount} grids for world {worldId}.");
            return entityCache;
        }
    }
}
