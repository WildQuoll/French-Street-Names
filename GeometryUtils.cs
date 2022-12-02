using System;
using UnityEngine;

namespace FrenchStreetNames
{
    class GeometryUtils
    {
        // Returns orientation of ordered triplet ABC:
        //  -1: counter-clockwise
        //  0: colinear
        //  1: clockwise
        private static int GetTripletOrientation(Vector2 a, Vector2 b, Vector2 c)
        {
            double v = (b.y - a.y) * (c.x - b.x) - (b.x - a.x) * (c.y - b.y);

            if (v == 0)
            {
                return 0;
            }

            return (v > 0) ? 1 : -1;
        }

        // Assuming points ABC are colinear, returns true if segment AB contains point C.
        private static bool SegmentContainsColinearPoint(Vector2 a, Vector2 b, Vector2 c)
        {
            return c.x <= Math.Max(a.x, b.x) &&
                   c.x >= Math.Min(a.x, b.x) &&
                   c.y <= Math.Max(a.y, b.y) &&
                   c.y >= Math.Min(a.y, b.y);
        }

        public static bool SegmentsIntersect(Vector2 startPt1, Vector2 endPt1, Vector2 startPt2, Vector2 endPt2)
        {
            int orientation1 = GetTripletOrientation(startPt1, endPt1, startPt2);
            int orientation2 = GetTripletOrientation(startPt1, endPt1, endPt2);
            int orientation3 = GetTripletOrientation(startPt2, endPt2, startPt1);
            int orientation4 = GetTripletOrientation(startPt2, endPt2, endPt1);

            if (orientation1 != orientation2 && orientation3 != orientation4)
            {
                return true;
            }

            if ((orientation1 == 0 && SegmentContainsColinearPoint(startPt1, startPt2, endPt1)) ||
                (orientation2 == 0 && SegmentContainsColinearPoint(startPt1, endPt2, endPt1)) ||
                (orientation3 == 0 && SegmentContainsColinearPoint(startPt2, startPt1, endPt2)) ||
                (orientation4 == 0 && SegmentContainsColinearPoint(startPt2, endPt1, endPt2)))
            {
                return true;
            }

            return false; // Doesn't fall in any of the above cases
        }
    }
}