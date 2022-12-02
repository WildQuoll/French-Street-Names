using System.Collections.Generic;
using ColossalFramework.Math;
using ColossalFramework;
using UnityEngine;

namespace FrenchStreetNames
{
    public class NetHelper
    {
        NetManager manager;

        public NetHelper()
        {
            manager = Singleton<NetManager>.instance;
        }

        public bool IsValidSegment(ushort id)
        {
            return manager.m_segments.m_buffer[id].Info != null;
        }

        public NetSegment GetSegmentByID(ushort id)
        {
            return manager.m_segments.m_buffer[id];
        }

        public ushort GetSegmentNameSeed(ushort id)
        {
            return manager.m_segments.m_buffer[id].m_nameSeed;
        }

        public NetNode GetNodeByID(ushort id)
        {
            return manager.m_nodes.m_buffer[id];
        }

        public bool IsNodeDeadEnd(ushort id)
        {
            return (manager.m_nodes.m_buffer[id].m_flags & NetNode.Flags.End) != 0;
        }

        public bool IsNodeCrossroad(ushort id)
        {
            return manager.m_nodes.m_buffer[id].CountSegments() > 2;
        }

        public float GetSegmentTurningAngleDeg(ushort id)
        {
            var segment = manager.m_segments.m_buffer[id];
            return Vector3.Angle(segment.m_startDirection, -segment.m_endDirection);
        }

        public float GetSegmentLength(ushort id)
        {
            return manager.m_segments.m_buffer[id].m_averageLength;
        }

        public RoadElevation GetSegmentElevation(ushort id)
        {
            if (manager.m_segments.m_buffer[id].Info.m_netAI.IsOverground())
            {
                return RoadElevation.BRIDGE;
            }
            else if (manager.m_segments.m_buffer[id].Info.m_netAI.IsUnderground())
            {
                return RoadElevation.TUNNEL;
            }
            else
            {
                return RoadElevation.GROUND;
            }
        }

        public float GetSegmentTotalLaneLength(ushort id)
        {
            var segment = manager.m_segments.m_buffer[id];
            var laneCount = segment.Info.m_backwardVehicleLaneCount + segment.Info.m_forwardVehicleLaneCount;
            return segment.m_averageLength * laneCount;
        }

        public float GetStraightLineDistanceBetweenNodes(ushort id1, ushort id2)
        {
            var startNode = manager.m_nodes.m_buffer[id1];
            var endNode = manager.m_nodes.m_buffer[id2];

            return VectorUtils.LengthXZ(startNode.m_position - endNode.m_position);
        }

        public float AngleDegBetweenSegmentsAtNode(ushort nodeId, ushort segId1, ushort segId2)
        {
            var seg1 = manager.m_segments.m_buffer[segId1];
            var seg2 = manager.m_segments.m_buffer[segId2];

            var segDir1 = (seg1.m_startNode == nodeId) ? seg1.m_startDirection : seg1.m_endDirection;
            var segDir2 = (seg2.m_startNode == nodeId) ? seg2.m_startDirection : seg2.m_endDirection;

            return Vector3.Angle(segDir1, -segDir2);
        }

        public bool IsSegmentStraight(ushort id)
        {
            return manager.m_segments.m_buffer[id].IsStraight();
        }

        public float GetNodeElevation(ushort id)
        {
            return manager.m_nodes.m_buffer[id].m_position.y;
        }

        public float GetSegmentSlope(ushort id)
        {
            var segment = manager.m_segments.m_buffer[id];
            var startNodeElevation = manager.m_nodes.m_buffer[segment.m_startNode].m_position.y;
            var endNodeElevation = manager.m_nodes.m_buffer[segment.m_endNode].m_position.y;

            return Mathf.Abs(startNodeElevation - endNodeElevation) / segment.m_averageLength;
        }

        private static uint GetNumVehicleLanes(ref NetInfo info)
        {
            uint count = 0;

            var carLaneTypes = (NetInfo.LaneType.Vehicle | NetInfo.LaneType.TransportVehicle);
            var carVehicleTypes = (VehicleInfo.VehicleType.Car | VehicleInfo.VehicleType.Tram | VehicleInfo.VehicleType.Trolleybus);

            foreach (var lane in info.m_lanes)
            {
                if ((lane.m_laneType & carLaneTypes) != 0)
                {
                    if ((lane.m_vehicleType & carVehicleTypes) != 0)
                    {
                        count += 1;
                    }
                }
            }

            return count;
        }

        public RoadCategory CategoriseRoadSegment(ushort segmentId)
        {
            const float MINOR_MEDIUM_HALF_WIDTH_THRESHOLD = 5.9f; // ~1.5 cells
            const float MEDIUM_MAJOR_HALF_WIDTH_THRESHOLD = 10.1f; // ~2.5 cells

            const float TRAFFIC_CALMED_STREET_SPEED_THRESHOLD = 0.5f; // 25 km/h
            const float HIGHWAY_SPEED_THRESHOLD = 1.9f; // 95 km/h

            var segment = manager.m_segments.m_buffer[segmentId];

            var info = segment.Info;
            if (info == null || !info.m_netAI.GetType().IsSubclassOf(typeof(RoadBaseAI)))
            {
                return RoadCategory.NONE;
            }

            bool highwayRules = (info.m_netAI as RoadBaseAI).m_highwayRules;
            if (highwayRules &&
                (info.m_averageVehicleLaneSpeed > HIGHWAY_SPEED_THRESHOLD || !AllowsZoning(info)))
            {
                if (info.m_hasForwardVehicleLanes && info.m_hasBackwardVehicleLanes)
                {
                    return RoadCategory.MAJOR_RURAL;
                }
                else
                {
                    return RoadCategory.MOTORWAY;
                }
            }

            uint numVehicleLanes = GetNumVehicleLanes(ref info);

            if (numVehicleLanes == 0 && !info.m_hasPedestrianLanes)
            {
                return RoadCategory.NONE;
            }

            if (info.m_hasPedestrianLanes &&
                (numVehicleLanes == 0 || info.m_averageVehicleLaneSpeed <= TRAFFIC_CALMED_STREET_SPEED_THRESHOLD))
            {
                // Either true pedestrian or traffic-calmed streets.

                if (info.m_halfWidth < MINOR_MEDIUM_HALF_WIDTH_THRESHOLD)
                {
                    return RoadCategory.MINOR_PEDESTRIAN;
                }
                else if (info.m_halfWidth < MEDIUM_MAJOR_HALF_WIDTH_THRESHOLD)
                {
                    return RoadCategory.MEDIUM_PEDESTRIAN;
                }
                else
                {
                    return RoadCategory.MAJOR_PEDESTRIAN;
                }
            }

            if (!info.m_createPavement)
            {
                const float SLOW_RURAL_THRESHOLD = 0.8f; // 40 km/h
                const float FAST_RURAL_THRESHOLD = 1.4f; // 70 km/h

                if (info.m_averageVehicleLaneSpeed <= SLOW_RURAL_THRESHOLD)
                {
                    if (numVehicleLanes <= 2)
                    {
                        return RoadCategory.MINOR_RURAL;
                    }
                    else
                    {
                        return RoadCategory.MEDIUM_RURAL;
                    }
                }
                else if (info.m_averageVehicleLaneSpeed >= FAST_RURAL_THRESHOLD)
                {
                    return RoadCategory.MAJOR_RURAL;
                }
                else
                {
                    if (numVehicleLanes <= 2)
                    {
                        return RoadCategory.MEDIUM_RURAL;
                    }
                    else
                    {
                        return RoadCategory.MAJOR_RURAL;
                    }
                }
            }

            if (info.m_halfWidth < MINOR_MEDIUM_HALF_WIDTH_THRESHOLD)
            {
                return RoadCategory.MINOR_URBAN;
            }
            else if (info.m_halfWidth < MEDIUM_MAJOR_HALF_WIDTH_THRESHOLD || numVehicleLanes < 4)
            {
                return RoadCategory.MEDIUM_URBAN;
            }
            else
            {
                return RoadCategory.MAJOR_URBAN;
            }
        }

        public bool IsSegmentAttachedToNode(ushort segmentId, ushort nodeId)
        {
            return manager.m_segments.m_buffer[segmentId].m_startNode == nodeId ||
                manager.m_segments.m_buffer[segmentId].m_endNode == nodeId;
        }

        public ushort GetSegmentStartNodeId(ushort segmentId)
        {
            return manager.m_segments.m_buffer[segmentId].m_startNode;
        }

        public ushort GetSegmentEndNodeId(ushort segmentId)
        {
            return manager.m_segments.m_buffer[segmentId].m_endNode;
        }

        // Returns 2D position of a network node (i.e. without height coordinate).
        public Vector2 GetNodePosition2d(ushort nodeId)
        {
            return VectorUtils.XZ(manager.m_nodes.m_buffer[nodeId].m_position);
        }

        public Vector3 GetNodePosition(ushort nodeId)
        {
            return manager.m_nodes.m_buffer[nodeId].m_position;
        }

        public Vector3 GetSegmentStartDirection(ushort segmentId)
        {
            return manager.m_segments.m_buffer[segmentId].m_startDirection;
        }

        public Vector3 GetSegmentEndDirection(ushort segmentId)
        {
            return manager.m_segments.m_buffer[segmentId].m_endDirection;
        }

        public void SetSegmentNameSeed(ushort segmentId, ushort nameSeed)
        {
            manager.m_segments.m_buffer[segmentId].m_nameSeed = nameSeed;
        }

        public Vector2 GetSegmentOrientation(ushort segmentId)
        {
            var segment = manager.m_segments.m_buffer[segmentId];
            var startNodePosition = GetNodePosition2d(segment.m_startNode);
            var endNodePosition = GetNodePosition2d(segment.m_endNode);

            var orientation = startNodePosition - endNodePosition;
            return orientation.normalized;
        }

        public HashSet<ushort> GetClosestSegmentIds(ushort segmentId)
        {
            var segment = manager.m_segments.m_buffer[segmentId];

            var segmentsArray = new ushort[16];
            int count;
            manager.GetClosestSegments(segment.m_middlePosition, segmentsArray, out count);

            var segments = new HashSet<ushort>();

            for (int i = 0; i < count; ++i)
            {
                segments.Add(segmentsArray[i]);
            }

            return segments;
        }

        public bool IsOneWay(ushort segmentId)
        {
            var segment = manager.m_segments.m_buffer[segmentId];
            return segment.Info.m_hasForwardVehicleLanes ^ segment.Info.m_hasBackwardVehicleLanes;
        }

        public bool AllowsZoning(NetInfo info)
        {
            var roadAI = info.m_netAI as RoadAI; // null for bridges and tunnels
            return roadAI && roadAI.m_enableZoning;
        }
    }
}
