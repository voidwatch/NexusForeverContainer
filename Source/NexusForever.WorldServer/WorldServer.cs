using System;
using System.IO;
using System.Reflection;
using System.Threading;
using NLog;
using NexusForever.Shared;
using NexusForever.Shared.Configuration;
using NexusForever.Shared.Cryptography;
using NexusForever.Shared.Database;
using NexusForever.Shared.Game;
using NexusForever.Shared.GameTable;
using NexusForever.Shared.Network;
using NexusForever.Shared.Network.Message;
using NexusForever.WorldServer.Command;
using NexusForever.WorldServer.Command.Context;
using NexusForever.WorldServer.Game.RBAC;
using NexusForever.WorldServer.Game;
using NexusForever.WorldServer.Game.Achievement;
using NexusForever.WorldServer.Game.CharacterCache;
using NexusForever.WorldServer.Game.Entity;
using NexusForever.WorldServer.Game.Entity.Movement;
using NexusForever.WorldServer.Game.Entity.Network;
using NexusForever.WorldServer.Game.Guild;
using NexusForever.WorldServer.Game.Housing;
using NexusForever.WorldServer.Game.Map;
using NexusForever.WorldServer.Game.Prerequisite;
using NexusForever.WorldServer.Game.Quest;
using NexusForever.WorldServer.Game.Reputation;
using NexusForever.WorldServer.Game.Social;
using NexusForever.WorldServer.Game.Spell;
using NexusForever.WorldServer.Game.Storefront;
using NexusForever.WorldServer.Game.TextFilter;
using NexusForever.WorldServer.Network;

namespace NexusForever.WorldServer
{
    internal static class WorldServer
    {
        #if DEBUG
        private const string Title = "NexusForever: World Server (DEBUG)";
        #else
        private const string Title = "NexusForever: World Server (RELEASE)";
        #endif

        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Internal unique id of the realm.
        /// </summary>
        public static ushort RealmId { get; private set; }

        /// <summary>
        /// Realm message of the day that is shown to players on login.
        /// </summary>
        public static string RealmMotd { get; set; }

        private static void Main()
        {
            Directory.SetCurrentDirectory(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location));

            Console.Title = Title;
            log.Info("Initialising...");

            ConfigurationManager<WorldServerConfiguration>.Instance.Initialise("WorldServer.json");
            RealmId   = ConfigurationManager<WorldServerConfiguration>.Instance.Config.RealmId;
            RealmMotd = ConfigurationManager<WorldServerConfiguration>.Instance.Config.MessageOfTheDay;

            DatabaseManager.Instance.Initialise(ConfigurationManager<WorldServerConfiguration>.Instance.Config.Database);
            DatabaseManager.Instance.Migrate();

            // optional: create/update this realm's row in the auth database from the environment (used by the container setup)
            string realmHost = Environment.GetEnvironmentVariable("NF_REALM_HOST");
            if (!string.IsNullOrWhiteSpace(realmHost))
            {
                string realmName = Environment.GetEnvironmentVariable("NF_REALM_NAME") ?? "NexusForever";
                ushort realmPort = ConfigurationManager<WorldServerConfiguration>.Instance.Config.Network.Port;
                log.Info($"Registering realm {RealmId} \"{realmName}\" at {realmHost}:{realmPort}");
                DatabaseManager.Instance.AuthDatabase.EnsureRealm((byte)RealmId, realmName, realmHost, realmPort);
            }

            // one-shot mode used by setup.sh: create an account from the environment and exit before any game data is loaded
            string newAccountEmail    = Environment.GetEnvironmentVariable("NF_CREATE_ACCOUNT_EMAIL");
            string newAccountPassword = Environment.GetEnvironmentVariable("NF_CREATE_ACCOUNT_PASSWORD");
            if (!string.IsNullOrWhiteSpace(newAccountEmail) && !string.IsNullOrEmpty(newAccountPassword))
            {
                newAccountEmail = newAccountEmail.Trim().ToLowerInvariant();
                if (DatabaseManager.Instance.AuthDatabase.AccountExists(newAccountEmail))
                {
                    log.Warn($"Account {newAccountEmail} already exists.");
                    LogManager.Shutdown();
                    Environment.Exit(2);
                }

                (string salt, string verifier) = PasswordProvider.GenerateSaltAndVerifier(newAccountEmail, newAccountPassword);
                DatabaseManager.Instance.AuthDatabase.CreateAccount(newAccountEmail, salt, verifier);
                log.Info($"Account {newAccountEmail} created successfully.");
                LogManager.Shutdown();
                Environment.Exit(0);
            }

            // RBACManager must be initialised before CommandManager
            RBACManager.Instance.Initialise();
            CommandManager.Instance.Initialise();

            DisableManager.Instance.Initialise();

            GameTableManager.Instance.Initialise();
            BaseMapManager.Instance.Initialise();
            SearchManager.Instance.Initialise();
            EntityManager.Instance.Initialise();
            EntityCommandManager.Instance.Initialise();
            EntityCacheManager.Instance.Initialise();
            FactionManager.Instance.Initialise();
            GlobalMovementManager.Instance.Initialise();

            GlobalChatManager.Instance.Initialise(); // must be initialised before guilds
            GlobalAchievementManager.Instance.Initialise(); // must be initialised before guilds
            GlobalGuildManager.Instance.Initialise(); // must be initialised before residences
            CharacterManager.Instance.Initialise(); // must be initialised before residences
            GlobalResidenceManager.Instance.Initialise();
            GlobalGuildManager.Instance.ValidateCommunityResidences();

            AssetManager.Instance.Initialise();
            ItemManager.Instance.Initialise();
            PrerequisiteManager.Instance.Initialise();
            GlobalSpellManager.Instance.Initialise();
            GlobalQuestManager.Instance.Initialise();

            GlobalStorefrontManager.Instance.Initialise();
            ServerManager.Instance.Initialise(RealmId); 

            MessageManager.Instance.Initialise();
            NetworkManager<WorldSession>.Instance.Initialise(ConfigurationManager<WorldServerConfiguration>.Instance.Config.Network);

            TextFilterManager.Instance.Initialise();

            WorldManager.Instance.Initialise(lastTick =>
            {
                // NetworkManager must be first and MapManager must come before everything else
                NetworkManager<WorldSession>.Instance.Update(lastTick);
                MapManager.Instance.Update(lastTick);

                BuybackManager.Instance.Update(lastTick);
                GlobalQuestManager.Instance.Update(lastTick);
                GlobalGuildManager.Instance.Update(lastTick);
                GlobalResidenceManager.Instance.Update(lastTick); // must be after guild update
                GlobalChatManager.Instance.Update(lastTick);

                // process commands after everything else in the tick has processed
                CommandManager.Instance.Update(lastTick);
            });

            using (WorldServerEmbeddedWebServer.Initialise())
            {
                log.Info("Ready!");

                while (true)
                {
                    Console.Write(">> ");
                    string line = Console.ReadLine();

                    // stdin is closed or absent (container without -i, systemd, nohup), ReadLine returns null immediately
                    // forever, which would spin a core and flood the command queue. Park the main thread instead,
                    // the world and network threads keep running.
                    if (line == null)
                    {
                        log.Info("No console input available, running headless.");
                        Thread.Sleep(Timeout.Infinite);
                    }

                    CommandManager.Instance.HandleCommandDelay(new ConsoleCommandContext(), line);
                }
            }
        }
    }
}
