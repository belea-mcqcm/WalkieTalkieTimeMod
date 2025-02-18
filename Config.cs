using BepInEx.Configuration;
using GameNetcodeStuff;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization.Formatters.Binary;
using Unity.Collections;
using Unity.Netcode;

namespace WalkieTalkieTimeMod
{
    [Serializable]
    public class SyncedInstance<T>
    {
        internal static CustomMessagingManager MessageManager => NetworkManager.Singleton.CustomMessagingManager;
        internal static bool IsClient => NetworkManager.Singleton.IsClient;
        internal static bool IsHost => NetworkManager.Singleton.IsHost;

        [NonSerialized]
        protected static int IntSize = 4;

        public static T Default { get; private set; }
        public static T Instance { get; private set; }

        public static bool Synced { get; internal set; }

        protected void InitInstance(T instance)
        {
            Default = instance;
            Instance = instance;

            IntSize = sizeof(int);
        }

        internal static void SyncInstance(byte[] data)
        {
            Instance = DeserializeFromBytes(data);
            Synced = true;
        }

        internal static void RevertSync()
        {
            Instance = Default;
            Synced = false;
        }

        public static byte[] SerializeToBytes(T val)
        {
            BinaryFormatter bf = new BinaryFormatter();
            MemoryStream stream = new MemoryStream();

            try
            {
                bf.Serialize(stream, val);
                return stream.ToArray();
            }
            catch (Exception e)
            {
                PluginBase.Instance.mls.LogError($"Error serializing instance: {e}");
                return null;
            }
        }

        public static T DeserializeFromBytes(byte[] data)
        {
            BinaryFormatter bf = new BinaryFormatter();
            MemoryStream stream = new MemoryStream(data);

            try
            {
                return (T)bf.Deserialize(stream);
            }
            catch (Exception e)
            {
                PluginBase.Instance.mls.LogError($"Error deserializing instance: {e}");
                return default;
            }
        }
    }

    [Serializable]
    class WTTConfig : SyncedInstance<WTTConfig>
    {
        public ConfigEntry<bool> ShowTimeOnlyWithWalkieTalkie;

        public WTTConfig(ConfigFile configFile)
        {
            InitInstance(this);

            configFile.SaveOnConfigSet = false;

            ShowTimeOnlyWithWalkieTalkie = configFile.Bind("General", "ShowTimeOnlyWithWalkieTalkie", false, new ConfigDescription("Time is always shown outside by default. Make game harder by only showing time by holding an active walkie talkie."));

            ClearOrphanedEntries(configFile);
            configFile.Save();
            configFile.SaveOnConfigSet = true;
        }

        static void ClearOrphanedEntries(ConfigFile cfg)
        {
            PropertyInfo orphanedEntriesProp = AccessTools.Property(typeof(ConfigFile), "OrphanedEntries");
            var orphanedEntries = (Dictionary<ConfigDefinition, string>)orphanedEntriesProp.GetValue(cfg);
            orphanedEntries.Clear();
        }

        public static void RequestSync()
        {
            if (!IsClient) return;

            FastBufferWriter stream = new FastBufferWriter(IntSize, Allocator.Temp);
            MessageManager.SendNamedMessage("WTT_OnRequestConfigSync", 0uL, stream);
        }

        public static void OnRequestSync(ulong clientId, FastBufferReader _)
        {
            if (!IsHost) return;

            PluginBase.Instance.mls.LogInfo($"Config sync request received from client: {clientId}");

            byte[] data = SerializeToBytes(Instance);
            int trueLength = data.Length;
            int fbwLength = FastBufferWriter.GetWriteSize(data) + IntSize;

            FastBufferWriter stream = new FastBufferWriter(fbwLength, Allocator.Temp);

            try
            {
                stream.WriteValueSafe(in trueLength, default);
                stream.WriteBytesSafe(data);

                MessageManager.SendNamedMessage("WTT_OnReceiveConfigSync", clientId, stream);
            }
            catch (Exception e)
            {
                PluginBase.Instance.mls.LogInfo($"Error occurred syncing config with client: {clientId}\n{e}");
            }
        }

        public static void OnReceiveSync(ulong _, FastBufferReader reader)
        {
            if (!reader.TryBeginRead(IntSize))
            {
                PluginBase.Instance.mls.LogError("Config sync error: Could not begin reading buffer.");
                return;
            }

            reader.ReadValueSafe(out int length, default);
            if (!reader.TryBeginRead(length))
            {
                PluginBase.Instance.mls.LogError("Config sync error: Host could not sync.");
                return;
            }

            byte[] data = new byte[length];
            reader.ReadBytesSafe(ref data, length);

            SyncInstance(data);

            PluginBase.Instance.mls.LogInfo("Successfully synced config with host.");
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(PlayerControllerB), "ConnectClientToPlayerObject")]
        public static void InitializeLocalPlayer()
        {
            if (IsHost)
            {
                MessageManager.RegisterNamedMessageHandler("WTT_OnRequestConfigSync", OnRequestSync);
                Synced = true;

                return;
            }

            Synced = false;
            MessageManager.RegisterNamedMessageHandler("WTT_OnReceiveConfigSync", OnReceiveSync);
            RequestSync();
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameNetworkManager), "StartDisconnect")]
        public static void PlayerLeave()
        {
            RevertSync();
        }
    }
}
