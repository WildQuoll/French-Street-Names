using ColossalFramework;
using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FrenchStreetNames
{
    // This patch ensures that whenever a road segment is updated, all other segments which form part
    // of the same named road are also updated.
    [HarmonyPriority(Priority.High)]
    [HarmonyPatch(typeof(NetManager), "SimulationStepImpl")]
    class NetManagerSimulationStepImplPatch
    {
        [HarmonyPrefix]
        public static void Prefix(int subStep, bool ___m_segmentsUpdated, ulong[] ___m_updatedSegments, out HashSet<ushort> __state)
        {
            // __state will contain IDs of all road segments whose name may require updating.
            if (!___m_segmentsUpdated)
            {
                __state = null;
                return;
            }

            RoadNameGenerator.ClearCache();

            var segmentIds = GetCorrespondingSegmentIds(___m_updatedSegments);

            __state = new HashSet<ushort>();

            var netHelper = new NetHelper();
            while(segmentIds.Count > 0)
            {
                var segmentId = segmentIds.First();

                if ( !netHelper.IsValidSegment(segmentId) )
                {
                    segmentIds.Remove(segmentId);
                    continue;
                }

                var road = new Road(segmentId);
                __state.UnionWith(road.m_segmentIds);
                segmentIds.ExceptWith(road.m_segmentIds);
            }

            // Might be able to optimise it further by subtracting 'segmentIds' (before they were emptied) from '__state', but it doesn't seem necessary.
        }

        [HarmonyPostfix]
        public static void Postfix(int subStep, HashSet<ushort> __state)
        {
            if (__state != null)
            {
                NetManager manager = Singleton<NetManager>.instance;
                foreach (var id in __state)
                {
                    manager.UpdateSegmentRenderer(id, false);
                }
            }
        }

        private static HashSet<ushort> GetCorrespondingSegmentIds(ulong[] segmentInternalIdStore)
        {
            var segmentIds = new HashSet<ushort>();

            // From NetManager.SimulationStepImpl
            int count = segmentInternalIdStore.Length;
            for (int i = 0; i < count; i++)
            {
                ulong segmentInternalStoreId = segmentInternalIdStore[i];
                if (segmentInternalStoreId != 0UL)
                {
                    for (int m = 0; m < 64; m++)
                    {
                        if ((segmentInternalStoreId & 1UL << m) != 0UL)
                        {
                            segmentIds.Add((ushort)(i << 6 | m));
                        }
                    }
                }
            }

            return segmentIds;
        }
    }
}
