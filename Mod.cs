using ICities;
using CitiesHarmony.API;
using System.IO;
using System.Reflection;
using UnityEngine;
using ColossalFramework.Plugins;

namespace FrenchStreetNames
{
    public class Mod : IUserMod
    {
        public string Name => "French Street Names / Noms des rues françaises";
        public string Description => "Replaces generic street names with French-flavoured ones.";

        public void OnEnabled()
        {
            HarmonyHelper.DoOnHarmonyReady(() => Patcher.PatchAll());
        }

        public void OnDisabled()
        {
            if (HarmonyHelper.IsHarmonyInstalled) Patcher.UnpatchAll();
        }

        public static string Identifier = "WQ.FSN/";

        public static string GetModDirectory()
        {
            return PluginManager.instance.FindPluginInfo(Assembly.GetExecutingAssembly()).modPath;
        }
    }
}
