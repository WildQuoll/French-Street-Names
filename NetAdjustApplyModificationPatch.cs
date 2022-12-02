using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ColossalFramework;
using HarmonyLib;
using UnityEngine;

namespace FrenchStreetNames
{
    // This patch ensures all relevant roads have their names updated
    // when the 'Adjust roads' tool is used.

    [HarmonyPriority(Priority.VeryHigh)]
    [HarmonyPatch(typeof(NetAdjust), nameof(NetAdjust.ApplyModification))]
    class NetAdjustApplyModificationPatch
    {
        [HarmonyPrefix]
        public static void Prefix(int index, HashSet<ushort> ___m_includedSegments, HashSet<ushort> ___m_originalSegments, out HashSet<ushort> __state)
        {
            RoadNameGenerator.ClearCache();

            __state = new HashSet<ushort>(); // segment IDs to update in postfix

            var netHelper = new NetHelper();

            var segmentIdsToProcess = new HashSet<ushort>();
            segmentIdsToProcess.UnionWith(___m_includedSegments);
            segmentIdsToProcess.UnionWith(___m_originalSegments);

            // By default, the game will only update those segments which have been assigned a new nameseed.
            // We also need to update all other segments belonging to the affected roads, as the roads themselves may have been renamed.
            while (segmentIdsToProcess.Count > 0)
            {
                var id = segmentIdsToProcess.First();

                if (!netHelper.IsValidSegment(id))
                {
                    segmentIdsToProcess.Remove(id);
                    continue;
                }

                var road = new Road(id);

                __state.UnionWith(road.m_segmentIds);
                segmentIdsToProcess.ExceptWith(road.m_segmentIds);
            }
        }

        [HarmonyPostfix]
        public static void Postfix(int index, HashSet<ushort> ___m_includedSegments, HashSet<ushort> ___m_originalSegments, HashSet<ushort> __state)
        {
            NetManager manager = Singleton<NetManager>.instance;
            foreach(var id in __state)
            {
                manager.UpdateSegmentRenderer(id, false);
            }
        }
    }
}
