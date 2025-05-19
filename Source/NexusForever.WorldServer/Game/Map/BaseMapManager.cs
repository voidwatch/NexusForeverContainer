﻿using System.Collections.Generic;
using System.IO;
using NexusForever.Shared;
using NexusForever.Shared.Configuration;
using NexusForever.Shared.GameTable;
using NexusForever.Shared.GameTable.Model;
using NexusForever.Shared.IO.Map;
using NLog;

namespace NexusForever.WorldServer.Game.Map
{
    public sealed class BaseMapManager : Singleton<BaseMapManager>
    {
        private static readonly Logger log = LogManager.GetCurrentClassLogger();

        private readonly Dictionary<string, MapFile> mapFiles = new();

        private BaseMapManager()
        {
        }

        public void Initialise()
        {
            ValidateMapFiles();
            PreCacheMapFiles();
        }

        private void ValidateMapFiles()
        {
            log.Info("Validating map files...");

            string mapPath = ConfigurationManager<WorldServerConfiguration>.Instance.Config.Map.MapPath;
            if (mapPath == null || !Directory.Exists(mapPath))
                throw new DirectoryNotFoundException("Invalid path to base maps! Make sure you have set it in the configuration file.");

            foreach (string fileName in Directory.EnumerateFiles(mapPath, "*.nfmap"))
            {
                using FileStream stream = File.OpenRead(fileName);
                using var reader = new BinaryReader(stream);
                var mapFile = new MapFile();
                mapFile.ReadHeader(reader);
            }
        }

        private void PreCacheMapFiles()
        {
            log.Info("Caching map files...");

            List<ushort> precachedBaseMaps = ConfigurationManager<WorldServerConfiguration>.Instance.Config.Map.PrecacheBaseMaps;
            if (precachedBaseMaps == null)
                return;

            foreach (ushort worldId in precachedBaseMaps)
            {
                WorldEntry entry = GameTableManager.Instance.World.GetEntry(worldId);
                if (entry == null)
                    throw new ConfigurationException($"Invalid world id {worldId} supplied for precached base maps!");

                LoadBaseMap(entry.AssetPath);
            }
        }

        /// <summary>
        /// Returns an existing <see cref="MapFile"/> for the supplied asset, if it doesn't exist a new one will be created from disk.
        /// </summary>
        public MapFile GetBaseMap(string assetPath)
        {
            if (mapFiles.TryGetValue(assetPath, out MapFile mapFile))
                return mapFile;

            return LoadBaseMap(assetPath);
        }

        private MapFile LoadBaseMap(string assetPath)
        {
            string mapPath  = ConfigurationManager<WorldServerConfiguration>.Instance.Config.Map.MapPath;
            string asset    = Path.Combine(mapPath, Path.GetFileName(assetPath));
            string filePath = Path.ChangeExtension(asset, ".nfmap");

            using FileStream stream = File.OpenRead(filePath);
            using var reader = new BinaryReader(stream);
            var mapFile = new MapFile();
            mapFiles.Add(assetPath, mapFile);
            mapFile.Read(reader);

            log.Trace($"Initialised base map file for asset {assetPath}.");
            return mapFile;
        }
    }
}
