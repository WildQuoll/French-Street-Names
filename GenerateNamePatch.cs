using HarmonyLib;
using UnityEngine;

namespace FrenchStreetNames
{
    public static class RoadNameGenerator
    {
        private static RoadNameCache roadCache = new RoadNameCache();
        private static RoadNameCache tunnelCache = new RoadNameCache();
        private static RoadNameCache bridgeCache = new RoadNameCache();

        public static void ClearCache()
        {
            roadCache.Clear();
            tunnelCache.Clear();
            bridgeCache.Clear();
        }

        private static RoadNameCache GetCache(RoadElevation elevation)
        {
            switch (elevation)
            {
                case RoadElevation.GROUND:
                    return roadCache;
                case RoadElevation.BRIDGE:
                    return bridgeCache;
                case RoadElevation.TUNNEL:
                    return tunnelCache;
            }

            // Shouldn't end up here
            return roadCache;
        }

        public static string GenerateName(ushort segmentID, RoadElevation elevation)
        {
            var cache = GetCache(elevation);

            string cachedName = cache.FindName(segmentID);
            if (cachedName == null)
            {
                var road = new Road(segmentID);

                if (road.m_predominantCategory == RoadCategory.MOTORWAY)
                {
                    NameListManager.TryToMatchToExistingMotorway(segmentID, ref road);
                }

                string name = NameListManager.GenerateRoadName(ref road, elevation);
                cache.AddToCache(road, name);
                return name;
            }
            else
            {
                return cachedName;
            }
        }
    }

    [HarmonyPriority(Priority.High)]
    [HarmonyPatch(typeof(RoadAI), nameof(RoadAI.GenerateName))]
    public static class GenerateRoadNamePatch
    {
        [HarmonyPostfix]
        public static void Postfix(ushort segmentID, ref NetSegment data, NetInfo ___m_info, ref string __result)
        {
            __result = RoadNameGenerator.GenerateName(segmentID, RoadElevation.GROUND);
        }
    }

    [HarmonyPriority(Priority.High)]
    [HarmonyPatch(typeof(RoadTunnelAI), nameof(RoadTunnelAI.GenerateName))]
    public static class GenerateRoadTunnelNamePatch
    {
        [HarmonyPostfix]
        public static void Postfix(ushort segmentID, ref NetSegment data, NetInfo ___m_info, ref string __result)
        {
            __result = RoadNameGenerator.GenerateName(segmentID, RoadElevation.TUNNEL);
        }
    }

    [HarmonyPriority(Priority.High)]
    [HarmonyPatch(typeof(RoadBridgeAI), nameof(RoadBridgeAI.GenerateName))]
    public static class GenerateRoadBridgeNamePatch
    {
        [HarmonyPostfix]
        public static void Postfix(ushort segmentID, ref NetSegment data, NetInfo ___m_info, ref string __result)
        {
            __result = RoadNameGenerator.GenerateName(segmentID, RoadElevation.BRIDGE);
        }
    }
}