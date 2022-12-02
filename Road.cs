using System;
using System.Collections.Generic;
using ColossalFramework;
using ColossalFramework.Math;
using UnityEngine;

namespace FrenchStreetNames
{
    public enum LoopShape
    {
        SQUARE,
        OVAL,
        CIRCLE,
        IRREGULAR_LOOP
    }

    [Flags]
    public enum RoadFeature
    {
        NONE = 0,
        VALLEY = 1,
        DEADEND = 2,
        NEAR_LOOP = 4,
        LOOP = 8,
        WATERFRONT = 16,
        NEAR_WATER = 32,
        CROSSES_BRIDGE = 64,
        CROSSES_TUNNEL = 128,
        CROSSES_WATER = 256,// for bridges and tunnels
        ONE_WAY = 512,
        SHORT = 1024,
        LONG = 2048,
        STEEP = 4096,
        SPARSE_INTERSECTIONS = 8192
    }

    [Flags]
    public enum RoadElevation
    {
        NONE = 0,

        GROUND = 1,
        BRIDGE = 2,
        TUNNEL = 4,

        ALL = ~0
    }

    [Flags]
    public enum RoadCategory
    {
        NONE = 0,

        MINOR_PEDESTRIAN = 1,
        MEDIUM_PEDESTRIAN = 2,
        MAJOR_PEDESTRIAN = 4,
        MINOR_URBAN = 8,
        MEDIUM_URBAN = 16,
        MAJOR_URBAN = 32,
        MINOR_RURAL = 64,
        MEDIUM_RURAL = 128,
        MAJOR_RURAL = 256,
        MOTORWAY = 512,
        SQUARE = 1024,
        CIRCLE = 2048,
        OVAL = 4096,

        ALL = ~0
    }

    public class Road
    {
        public ushort m_nameSeed;
        public HashSet<ushort> m_segmentIds;
        public HashSet<ushort> m_nodeIds;

        // 0 elements for loops, 1 for P-shaped roads, 2 for linear
        public List<ushort> m_endNodeIds;

        public bool m_selfIntersects = false; // The road crosses itself OR forms a P-shape
        public float m_sharpestAngleDegAtNode = 0.0f; // The sharpest turn that the road makes at any NODE

        public float m_length = 0.0f;

        public RoadFeature m_roadFeatures = RoadFeature.NONE;

        public RoadCategory m_predominantCategory;

        public Road(ushort initialSegmentID)
        {
            var netHelper = new NetHelper();

            m_nameSeed = netHelper.GetSegmentNameSeed(initialSegmentID);
            DiscoverNodesAndSegments(ref netHelper, initialSegmentID,
                                     out m_nodeIds, out m_endNodeIds,
                                     out m_segmentIds, out m_selfIntersects, out m_sharpestAngleDegAtNode);

            m_predominantCategory = DetermineCategory(ref netHelper, ref m_segmentIds);

            int segmentCount = m_segmentIds.Count;

            if (IsOneWay(m_segmentIds))
            {
                m_roadFeatures |= RoadFeature.ONE_WAY;
            }

            m_length = CalculateLength(m_segmentIds);

            if (m_length < 200.0f)
            {
                m_roadFeatures |= RoadFeature.SHORT;
            }
            else if (m_length > 1600.0f)
            {
                m_roadFeatures |= RoadFeature.LONG;
            }

            if (AreIntersectionsSparse(netHelper, m_nodeIds, m_endNodeIds, m_length))
            {
                m_roadFeatures |= RoadFeature.SPARSE_INTERSECTIONS;
            }

            if (m_endNodeIds.Count == 0)
            {
                m_roadFeatures |= RoadFeature.LOOP;

                const float MAX_LOOP_LENGTH = 1600.0f;
                const float MAX_SQUARE_LENGTH = 600.0f;

                if (segmentCount <= 16 && m_length <= MAX_LOOP_LENGTH)
                {
                    var loopShape = IdentifyLoopShape(netHelper, m_nodeIds, m_segmentIds);

                    switch (loopShape)
                    {
                        case LoopShape.SQUARE:
                            if (m_length <= MAX_SQUARE_LENGTH)
                            {
                                m_predominantCategory = RoadCategory.SQUARE;
                            }
                            break;
                        case LoopShape.OVAL:
                            m_predominantCategory = RoadCategory.OVAL;
                            break;
                        case LoopShape.CIRCLE:
                            m_predominantCategory = RoadCategory.CIRCLE;
                            break;
                        case LoopShape.IRREGULAR_LOOP:
                        default:
                            break;
                    }
                }
            }

            if (m_predominantCategory == RoadCategory.MOTORWAY)
            {
                // Detect roundabouts (and squares) but nothing more
                return;
            }

            if (segmentCount <= 32 && (m_roadFeatures & RoadFeature.LOOP) == 0)
            {
                m_roadFeatures |= AnalyseTopography(ref netHelper, m_endNodeIds, ref m_segmentIds);

                if (IsSteep(ref netHelper, ref m_nodeIds, ref m_segmentIds))
                {
                    m_roadFeatures |= RoadFeature.STEEP;
                }
            }

            if (segmentCount <= 16 && (m_roadFeatures & RoadFeature.LOOP) == 0)
            {
                if ((m_predominantCategory & (RoadCategory.MAJOR_PEDESTRIAN | RoadCategory.MAJOR_URBAN | RoadCategory.MAJOR_RURAL)) == 0 &&
                    IsDeadEnd(ref netHelper, m_selfIntersects, ref m_nodeIds))
                {
                    m_roadFeatures |= RoadFeature.DEADEND;
                }

                // Crescent detection - disabled as no relevant prefixes used in France.

                //if (m_predominantCategory == RoadCategory.MINOR_URBAN &&
                //    IsCrescent(ref netHelper, m_sharpestAngleDegAtNode, ref m_endNodeIds, ref m_segmentIds))
                //{
                //    m_roadFeatures |= RoadFeature.CRESCENT;
                //}
            }

            if ((m_roadFeatures & RoadFeature.LOOP) == 0 &&
                IsNearLoop(ref netHelper, ref m_endNodeIds, ref m_segmentIds))
            {
                m_roadFeatures |= RoadFeature.NEAR_LOOP;
            }

            bool hasBridge, hasTunnel, crossesWater;
            CheckForBridgesAndTunnels(netHelper, m_segmentIds, m_nodeIds, out hasBridge, out hasTunnel, out crossesWater);

            if (hasBridge)
            {
                m_roadFeatures |= RoadFeature.CROSSES_BRIDGE;
            }

            if (hasTunnel)
            {
                m_roadFeatures |= RoadFeature.CROSSES_TUNNEL;
            }

            if (crossesWater && (hasBridge || hasTunnel))
            {
                m_roadFeatures |= RoadFeature.CROSSES_WATER;
            }
        }

        private static float CalculateLength(HashSet<ushort> segmentIds)
        {
            var helper = new NetHelper();

            float length = 0.0f;
            foreach (var segmentId in segmentIds)
            {
                length += helper.GetSegmentLength(segmentId);
            }

            return length;
        }

        public float CalculateTotalLaneLength()
        {
            var helper = new NetHelper();

            float length = 0.0f;
            foreach (var segmentId in m_segmentIds)
            {
                length += helper.GetSegmentTotalLaneLength(segmentId);
            }

            return length;
        }

        // returns 0 (horizontal) to 90 (vertical)
        public float CalculateMeanOrientationAngle()
        {
            var helper = new NetHelper();
            float orientationAngle = 0.0f;
            var horizontalVector = new Vector2(1.0f, 0.0f);

            foreach (var segmentId in m_segmentIds)
            {
                var angle = Vector2.Angle(helper.GetSegmentOrientation(segmentId), horizontalVector);

                if (angle > 90.0f)
                {
                    angle = 180.0f - angle;
                }

                var segmentLanesLength = helper.GetSegmentTotalLaneLength(segmentId);
                orientationAngle += angle * segmentLanesLength;
            }
            orientationAngle /= CalculateTotalLaneLength();
            return orientationAngle;
        }

        private static RoadCategory DetermineCategory(ref NetHelper netHelper, ref HashSet<ushort> segmentIds)
        {
            var categories = new Dictionary<RoadCategory, float>();

            foreach (var id in segmentIds)
            {
                var cat = netHelper.CategoriseRoadSegment(id);
                var length = netHelper.GetSegmentLength(id);

                if (!categories.ContainsKey(cat))
                {
                    categories.Add(cat, length);
                }
                else
                {
                    categories[cat] += netHelper.GetSegmentLength(id);
                }
            }

            float bestLength = 0.0f;
            RoadCategory bestCategory = RoadCategory.NONE;
            foreach (var cat in categories)
            {
                if (cat.Value > bestLength)
                {
                    bestLength = cat.Value;
                    bestCategory = cat.Key;
                }
            }

            return bestCategory;
        }

        private static void DiscoverNodesAndSegments(ref NetHelper netHelper,
                                                     ushort initialSegmentId,
                                                     out HashSet<ushort> discoveredNodeIds,
                                                     out List<ushort> endNodeIds,
                                                     out HashSet<ushort> discoveredSegmentIds,
                                                     out bool selfIntersects,
                                                     out float sharpestAngleDegAtNode)
        {
            discoveredNodeIds = new HashSet<ushort>();
            discoveredSegmentIds = new HashSet<ushort>();
            endNodeIds = new List<ushort>();
            selfIntersects = false;
            sharpestAngleDegAtNode = 0.0f;

            var segmentIdsToAnalyse = new List<ushort> { initialSegmentId };

            while (segmentIdsToAnalyse.Count > 0)
            {
                ushort segmentId = segmentIdsToAnalyse[segmentIdsToAnalyse.Count - 1];
                segmentIdsToAnalyse.RemoveAt(segmentIdsToAnalyse.Count - 1);

                discoveredSegmentIds.Add(segmentId);

                var segment = netHelper.GetSegmentByID(segmentId);

                foreach (var nodeId in new[] { segment.m_startNode, segment.m_endNode })
                {
                    if (discoveredNodeIds.Contains(nodeId))
                    {
                        continue;
                    }

                    discoveredNodeIds.Add(nodeId);

                    var node = netHelper.GetNodeByID(nodeId);
                    var otherSegmentIds = FindSameRoadSegmentIds(ref netHelper, ref node, segmentId);

                    if (otherSegmentIds.Count > 1)
                    {
                        selfIntersects = true;
                    }
                    else if (otherSegmentIds.Count == 1)
                    {
                        sharpestAngleDegAtNode = Mathf.Max(sharpestAngleDegAtNode, netHelper.AngleDegBetweenSegmentsAtNode(nodeId, segmentId, otherSegmentIds[0]));
                    }
                    else if (otherSegmentIds.Count == 0)
                    {
                        endNodeIds.Add(nodeId);
                    }

                    foreach (var otherSegmentId in otherSegmentIds)
                    {
                        if (!discoveredSegmentIds.Contains(otherSegmentId))
                        {
                            discoveredSegmentIds.Add(otherSegmentId);
                            segmentIdsToAnalyse.Add(otherSegmentId);
                        }
                    }
                }
            }
        }

        private static List<ushort> FindSameRoadSegmentIds(ref NetHelper netHelper, ref NetNode node, ushort currentSegmentId)
        {
            var segmentIds = new List<ushort>();

            ushort roadId = netHelper.GetSegmentNameSeed(currentSegmentId);

            for (int i = 0; i < 8; ++i)
            {
                ushort segmentId = node.GetSegment(i);
                if (segmentId != 0 && segmentId != currentSegmentId)
                {
                    if (netHelper.GetSegmentNameSeed(segmentId) == roadId)
                    {
                        segmentIds.Add(segmentId);
                    }
                }
            }

            return segmentIds;
        }

        private static void CheckForBridgesAndTunnels(NetHelper helper, HashSet<ushort> segmentIds, HashSet<ushort> nodeIds, out bool hasBridge, out bool hasTunnel, out bool crossesWater)
        {
            hasBridge = false;
            hasTunnel = false;
            crossesWater = false;

            foreach (var segmentId in segmentIds)
            {
                var segment = helper.GetSegmentByID(segmentId);

                hasBridge |= segment.Info.m_netAI.IsOverground();
                hasTunnel |= segment.Info.m_netAI.IsUnderground();
            }

            if (hasBridge || hasTunnel)
            {
                var manager = Singleton<TerrainManager>.instance;
                foreach (var nodeId in nodeIds)
                {
                    var nodePos = helper.GetNodePosition2d(nodeId);

                    if (manager.HasWater(nodePos))
                    {
                        crossesWater = true;
                        break;
                    }
                }
            }
        }

        private static bool IsDeadEnd(ref NetHelper netHelper, bool selfIntersects, ref HashSet<ushort> nodeIds)
        {
            if (selfIntersects)
            {
                return false;
            }

            int numCrossroads = 0;
            int numDeadEnds = 0;
            foreach (var nodeId in nodeIds)
            {
                if (netHelper.IsNodeDeadEnd(nodeId))
                {
                    numDeadEnds += 1;
                }

                if (netHelper.IsNodeCrossroad(nodeId))
                {
                    numCrossroads += 1;
                }
            }

            return numDeadEnds == 1 && numCrossroads < 2;
        }

        private static bool IsCloseToCircle(NetHelper netHelper, HashSet<ushort> nodeIds)
        {
            // See if the axis-aligned bounding box is a square
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);

            foreach (var id in nodeIds)
            {
                var pos = netHelper.GetNodePosition(id);

                min.x = Mathf.Min(min.x, pos.x);
                min.y = Mathf.Min(min.y, pos.z);

                max.x = Mathf.Max(max.x, pos.x);
                max.y = Mathf.Max(max.y, pos.z);
            }

            float width = max.x - min.x;
            float height = max.y - min.y;

            float ratio = Mathf.Min(width, height) / Mathf.Max(width, height);

            if (ratio < 0.9f)
            {
                // Not a square -> not a circle
                return false;
            }

            // Now try a bounding box rotated by 45 degrees
            var min45 = new Vector2(float.MaxValue, float.MaxValue);
            var max45 = new Vector2(float.MinValue, float.MinValue);

            float sin = 0.8509f;
            float cos = 0.5253f;

            foreach (var id in nodeIds)
            {
                var pos = netHelper.GetNodePosition(id);
                var pos45 = new Vector2(pos.x * cos - pos.z * sin,
                                        pos.z * cos + pos.x * sin);

                min45.x = Mathf.Min(min45.x, pos45.x);
                min45.y = Mathf.Min(min45.y, pos45.y);

                max45.x = Mathf.Max(max45.x, pos45.x);
                max45.y = Mathf.Max(max45.y, pos45.y);
            }

            float width45 = max45.x - min45.x;
            float height45 = max45.y - min45.y;

            float ratio45 = Mathf.Min(width45, height45) / Mathf.Max(width45, height45);

            return ratio45 >= 0.9f;
        }

        // Given a road known to form a closed loop, identifies its shape.
        private static LoopShape IdentifyLoopShape(NetHelper netHelper, HashSet<ushort> nodeIds, HashSet<ushort> segmentIds)
        {
            float combinedAngleDeg = 0.0f;
            foreach (var segmentId in segmentIds)
            {
                combinedAngleDeg += netHelper.GetSegmentTurningAngleDeg(segmentId);
            }

            if (combinedAngleDeg >= 315.0f)
            {
                if (combinedAngleDeg > 405.0f)
                {
                    return LoopShape.IRREGULAR_LOOP;
                }

                bool circle = IsCloseToCircle(netHelper, nodeIds);

                if (circle)
                {
                    return LoopShape.CIRCLE;
                }
                else
                {
                    return LoopShape.OVAL;
                }
            }

            return LoopShape.SQUARE;
        }

        //private static bool IsCrescent(ref NetHelper netHelper, float sharpestAngleDeg, ref List<ushort> endNodeIds, ref HashSet<ushort> segmentIds)
        //{
        //    if (endNodeIds.Count != 2 || sharpestAngleDeg > 10.0f)
        //    {
        //        return false;
        //    }

        //    float curvedLength = 0.0f;
        //    float combinedLength = 0.0f;
        //    foreach (var segmentId in segmentIds)
        //    {
        //        var length = netHelper.GetSegmentLength(segmentId);

        //        combinedLength += length;

        //        if (!netHelper.IsSegmentStraight(segmentId))
        //        {
        //            curvedLength += length;
        //        }
        //    }

        //    if (curvedLength / combinedLength < 0.5f)
        //    {
        //        return false;
        //    }

        //    float tortuosity = combinedLength / netHelper.GetStraightLineDistanceBetweenNodes(endNodeIds[0], endNodeIds[1]);

        //    return tortuosity > 1.05f;
        //}

        private static bool IsNearLoop(ref NetHelper netHelper,
                                       ref List<ushort> endNodeIds,
                                       ref HashSet<ushort> segmentIds)
        {
            if (endNodeIds.Count != 2)
            {
                // Closed loop
                return false;
            }

            float length = 0.0f;
            foreach (var segmentId in segmentIds)
            {
                length += netHelper.GetSegmentLength(segmentId);
            }

            float distanceBetweenNodes = netHelper.GetStraightLineDistanceBetweenNodes(endNodeIds[0], endNodeIds[1]);
            float tortuosity = distanceBetweenNodes > 0.0f ? length / distanceBetweenNodes : float.MaxValue;

            if (tortuosity < 2.5f)
            {
                // Not tortuous enough
                return false;
            }

            var startNodePos = netHelper.GetNodePosition2d(endNodeIds[0]);
            var endNodePos = netHelper.GetNodePosition2d(endNodeIds[1]);

            foreach (var segmentId in segmentIds)
            {
                if (netHelper.IsSegmentAttachedToNode(segmentId, endNodeIds[0]) ||
                    netHelper.IsSegmentAttachedToNode(segmentId, endNodeIds[1]))
                {
                    // Skip the first and last segment
                    continue;
                }

                if (GeometryUtils.SegmentsIntersect(startNodePos,
                                                    endNodePos,
                                                    netHelper.GetNodePosition2d(netHelper.GetSegmentStartNodeId(segmentId)),
                                                    netHelper.GetNodePosition2d(netHelper.GetSegmentEndNodeId(segmentId))))
                {
                    // S- or Z- shape, or similar
                    return false;
                }
            }

            return true;
        }

        private static bool IsSteep(ref NetHelper netHelper, ref HashSet<ushort> nodeIds, ref HashSet<ushort> segmentIds)
        {
            float minElevation = float.MaxValue, maxElevation = float.MinValue;
            float meanElevation = 0.0f;
            foreach (var nodeId in nodeIds)
            {
                var elevation = netHelper.GetNodeElevation(nodeId);
                minElevation = Mathf.Min(minElevation, elevation);
                maxElevation = Mathf.Max(maxElevation, elevation);
                meanElevation += elevation;
            }

            meanElevation /= nodeIds.Count;

            float hillyLength = 0.0f;
            float totalLength = 0.0f;
            foreach (var segmentId in segmentIds)
            {
                var segmentLength = netHelper.GetSegmentLength(segmentId);
                totalLength += segmentLength;

                var segmentSlope = netHelper.GetSegmentSlope(segmentId);
                if (segmentSlope >= 0.1f)
                {
                    hillyLength += segmentLength;
                }
            }

            float slope = (maxElevation - minElevation) / totalLength;

            return (slope >= 0.1f && hillyLength / totalLength > 0.7f);
        }

        private static Vector2 Rotate90Clockwise(Vector2 v)
        {
            return new Vector2(v.y, -v.x);
        }

        private static Vector2 Rotate90CounterClockwise(Vector2 v)
        {
            return new Vector2(-v.y, v.x);
        }

        private static Vector3 ToVector3(Vector2 v)
        {
            return new Vector3(v.x, 0.0f, v.y);
        }

        private static void AnalyseTopographyAtPosition(ref TerrainManager manager, ref NetHelper netHelper, Vector3 pos, float baseElevation, out bool waterFound, out float relativeElevation)
        {
            if (manager.HasWater(VectorUtils.XZ(pos)))
            {
                waterFound = true;
                relativeElevation = 0.0f;
            }
            else
            {
                waterFound = false;
                relativeElevation = manager.SampleDetailHeight(pos) - baseElevation;
            }
        }

        private static void AnalyseNodeTopography(ref NetHelper netHelper, Vector3 nodePos, Vector3 outwardDir, bool isEndNode, ref bool waterFound, ref int sideWaterCount, ref List<float> relativeSurroundingElevations)
        {
            float testDistance = 80.0f; // in metres
            TerrainManager terrainManager = Singleton<TerrainManager>.instance;

            var nodeElevation = terrainManager.SampleDetailHeight(nodePos);

            Vector2 nodePos2d = VectorUtils.XZ(nodePos);
            Vector2 outwardOffset2d = VectorUtils.XZ(outwardDir);
            outwardOffset2d.Normalize();
            outwardOffset2d *= testDistance;

            var testPositions = new List<Vector3>
            {
                ToVector3(nodePos2d + Rotate90Clockwise(outwardOffset2d)),
                ToVector3(nodePos2d + Rotate90CounterClockwise(outwardOffset2d)),
            };

            if (isEndNode)
            {
                testPositions.Add(ToVector3(nodePos2d + outwardOffset2d));
            }

            int i = 0;
            foreach (var testPos in testPositions)
            {
                bool waterAtPos;
                float relativeElevationAtPos;
                AnalyseTopographyAtPosition(ref terrainManager, ref netHelper, testPos, nodeElevation, out waterAtPos, out relativeElevationAtPos);

                waterFound |= waterAtPos;
                relativeSurroundingElevations.Add(relativeElevationAtPos);

                if (waterAtPos && i < 2)
                {
                    sideWaterCount += 1;
                }
            }

            if (isEndNode)
            {
                int discardSideCount = 0;
                AnalyseNodeTopography(ref netHelper, testPositions[2], outwardDir, false, ref waterFound, ref discardSideCount, ref relativeSurroundingElevations);
            }
        }

        private static bool AreIntersectionsSparse(NetHelper helper, HashSet<ushort> nodeIds, List<ushort> endNodeIds, float length)
        {
            const float THRESHOLD = 500.0f;
            if (length < THRESHOLD)
            {
                return false;
            }

            int allowedIntersections = (int)Mathf.Floor(length / THRESHOLD) - 1;

            foreach (var nodeId in nodeIds)
            {
                if (endNodeIds.Contains(nodeId))
                {
                    continue;
                }

                if (helper.IsNodeCrossroad(nodeId))
                {
                    allowedIntersections -= 1;

                    if (allowedIntersections < 0)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static RoadFeature AnalyseTopography(ref NetHelper netHelper, List<ushort> endNodeIds, ref HashSet<ushort> segmentIds)
        {
            var processedNodeIds = new List<ushort>();

            bool waterFound = false;
            int sideWaterCount = 0;
            var relativeSurroundingElevations = new List<float>();

            foreach (var segmentId in segmentIds)
            {
                var startNodeId = netHelper.GetSegmentStartNodeId(segmentId);

                if (!processedNodeIds.Contains(startNodeId))
                {
                    processedNodeIds.Add(startNodeId);
                    var startDir = netHelper.GetSegmentStartDirection(segmentId);
                    bool isEndNode = endNodeIds.Contains(startNodeId);
                    var nodePos = netHelper.GetNodePosition(startNodeId);
                    AnalyseNodeTopography(ref netHelper, nodePos, -startDir, isEndNode, ref waterFound, ref sideWaterCount, ref relativeSurroundingElevations);
                }

                var endNodeId = netHelper.GetSegmentEndNodeId(segmentId);

                if (!processedNodeIds.Contains(endNodeId))
                {
                    processedNodeIds.Add(endNodeId);
                    var endDir = netHelper.GetSegmentEndDirection(segmentId);
                    bool isEndNode = endNodeIds.Contains(endNodeId);
                    var nodePos = netHelper.GetNodePosition(endNodeId);
                    AnalyseNodeTopography(ref netHelper, nodePos, -endDir, isEndNode, ref waterFound, ref sideWaterCount, ref relativeSurroundingElevations);
                }
            }

            RoadFeature features = RoadFeature.NONE;

            if (waterFound)
            {
                features |= RoadFeature.NEAR_WATER;
            }

            var waterfrontThreshold = 0.8f * processedNodeIds.Count;
            if (sideWaterCount >= waterfrontThreshold)
            {
                features |= RoadFeature.WATERFRONT;
            }

            // Hill crest detection - disabled as no relevant prefixes used in France.

            //relativeSurroundingElevations.Sort();
            //if (relativeSurroundingElevations[0] <= -8.0f) // at least 1 node must be at least 8 metres below street level
            //{
            //    // Calculate mean elevation for the lowest lying 50% of surrounding nodes.
            //    float numNodesToAnalyse = Mathf.Ceil(relativeSurroundingElevations.Count / 2.0f);
            //    float meanElevation = 0.0f;
            //    for (int i = 0; i < numNodesToAnalyse; ++i)
            //    {
            //        meanElevation += relativeSurroundingElevations[i];
            //    }

            //    meanElevation /= numNodesToAnalyse;

            //    if (meanElevation <= -6.0f)
            //    {
            //        features |= RoadFeature.HILLCREST;
            //    }
            //}

            relativeSurroundingElevations.Sort((a, b) => b.CompareTo(a)); // descending
            if (relativeSurroundingElevations[0] >= 8.0f) // at least 1 node must be at least 8 metres above street level (10% incline)
            {
                // Calculate mean elevation for the highest lying 75% of surrounding nodes.
                float numNodesToAnalyse = Mathf.Ceil(relativeSurroundingElevations.Count * 0.75f);
                float meanElevation = 0.0f;
                for (int i = 0; i < numNodesToAnalyse; ++i)
                {
                    meanElevation += relativeSurroundingElevations[i];
                }

                meanElevation /= numNodesToAnalyse;

                if (meanElevation >= 6.0f) // 7.5% incline
                {
                    features |= RoadFeature.VALLEY;
                }
            }

            return features;
        }

        public void SetNameSeed(ushort nameSeed)
        {
            m_nameSeed = nameSeed;

            var helper = new NetHelper();
            foreach (var segmentId in m_segmentIds)
            {
                helper.SetSegmentNameSeed(segmentId, m_nameSeed);
            }
        }

        private static bool IsOneWay(HashSet<ushort> segmentIds)
        {
            var helper = new NetHelper();
            foreach (var segmentId in segmentIds)
            {
                if (!helper.IsOneWay(segmentId))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
