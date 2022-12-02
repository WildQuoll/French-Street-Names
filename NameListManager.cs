using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using ColossalFramework;
using ColossalFramework.Math;
using System;

namespace FrenchStreetNames
{
    static class NameListManager
    {
        private static readonly long SPECIFIC_INDEX_OFFSET = GenerateIndexOffsetForSpecifics();

        private static long GenerateIndexOffsetForSpecifics()
        {
            return Singleton<SimulationManager>.instance.m_metaData.m_startingDateTime.Ticks;
        }

        public static void TryToMatchToExistingMotorway(ushort segmentId, ref Road road)
        {
            var helper = new NetHelper();

            var thisSegmentNameSeed = helper.GetSegmentNameSeed(segmentId);

            HashSet<ushort> nearbySegmentIds = helper.GetClosestSegmentIds(segmentId);

            float bestScore = 0.0f;
            ushort bestNameSeed = 0;

            while (nearbySegmentIds.Count > 0)
            {
                var id = nearbySegmentIds.First();
                var seedId = helper.GetSegmentNameSeed(id);

                if (thisSegmentNameSeed == seedId)
                {
                    nearbySegmentIds.Remove(id);
                    continue;
                }

                var otherRoad = new Road(id);
                nearbySegmentIds.ExceptWith(otherRoad.m_segmentIds);

                if (otherRoad.m_predominantCategory != RoadCategory.MOTORWAY)
                {
                    continue;
                }

                var thisMotorway = new MotorwayInfo(road);
                var otherMotorway = new MotorwayInfo(otherRoad);

                float similarity = thisMotorway.CalculateSimilarity(otherMotorway);

                if (similarity > bestScore)
                {
                    bestScore = similarity;
                    bestNameSeed = otherRoad.m_nameSeed;
                }
            }

            if (bestScore > 0.75f)
            {
                road.SetNameSeed(bestNameSeed);
            }
        }

        private static string GenerateMotorwayName(ushort seed)
        {
            var randomiser = new Randomizer(seed);

            switch (seed % 7)
            {
                case 0:
                    return "N" + Math.Min(randomiser.UInt32(1, 999), randomiser.UInt32(1, 999));
                case 1:
                case 2:
                    return "A" + Math.Min(randomiser.UInt32(100, 999), randomiser.UInt32(100, 999));
                default:
                    return "A" + randomiser.UInt32(1, 99);
            }
        }

        public static string GenerateRoadName(ref Road road, RoadElevation elevation)
        {
            var seed = road.m_nameSeed;
            var category = road.m_predominantCategory;

            if (category == RoadCategory.NONE)
            {
                return "";
            }

            if (category == RoadCategory.MOTORWAY)
            {
                var laneLength = road.CalculateTotalLaneLength();

                if (laneLength < 2000.0f)
                {
                    return "";
                }

                return GenerateMotorwayName(seed);
            }

            Randomizer specificPartRandomiser = new Randomizer(seed);
            Randomizer genericPartRandomiser = new Randomizer(seed);

            string genericPart = GetRandomGenericPart(ref genericPartRandomiser, elevation, category, road.m_roadFeatures);

            string specificPart;

            int attempts = 0;
            do
            {
                specificPart = GetRandomSpecificPart(ref specificPartRandomiser);
                attempts += 1;

                if (attempts > 30)
                {
                    Debug.Log(Mod.Identifier + "Could not find a suitable specific!");
                    break;
                }
            }
            while (specificPart.ToUpperInvariant().Contains(genericPart.ToUpperInvariant()) ||
                   !IsValidSpecificPart(specificPart, elevation, category, road.m_roadFeatures));

            if (specificPart.Contains('|'))
            {
                var specificPartGenderVariants = specificPart.Split('|');
                bool genericIsMasculine = (ROAD_NAME_GENERICS_DATA[genericPart].gender == GrammaticalGender.Masculine);
                specificPart = specificPartGenderVariants[genericIsMasculine ? 0 : 1];
            }

            if (specificPart.EndsWith("$"))
            {
                return specificPart.TrimEnd('$');
            }

            return genericPart + " " + specificPart;
        }

        private static string GetRandomSpecificPart(ref Randomizer randomiser)
        {
            int index = randomiser.Int32(0, ROAD_NAME_SPECIFICS.Count - 1);
            index = (int)((index + SPECIFIC_INDEX_OFFSET) % ROAD_NAME_SPECIFICS.Count);

            return ROAD_NAME_SPECIFICS[index];
        }

        private static string GetRandomGenericPart(ref Randomizer randomiser, RoadElevation elevation, RoadCategory category, RoadFeature features)
        {
            if (!ROAD_NAME_GENERICS[elevation][category].ContainsKey(features))
            {
                ROAD_NAME_GENERICS[elevation][category].Add(features, new GenericPartList(ROAD_NAME_GENERICS_DATA, elevation, category, features));
            }

            return ROAD_NAME_GENERICS[elevation][category][features].GetRandomGenericPart(ref randomiser);
        }

        private static bool IsValidSpecificPart(string specific, RoadElevation elevation, RoadCategory category, RoadFeature features)
        {
            return ROAD_NAME_SPECIFICS_DATA[specific].IsValidFor(elevation, category, features);
        }

        class GenericPartList
        {
            public GenericPartList(Dictionary<string, GenericPartProperties> list, RoadElevation elevation, RoadCategory category, RoadFeature features)
            {
                //Debug.Log(Mod.Identifier + "Generating generics list for " + elevation + "/" + category + "/" + features);
                //var s = "";

                generics = new SortedList<uint, string>();
                uint combinedP = 0;
                foreach (var l in list)
                {
                    uint p = l.Value.GetProbability(elevation, category, features);

                    if (p > 0)
                    {
                        combinedP += p;
                        generics.Add(combinedP, l.Key);
                        //s += l.Key + ":" + combinedP + "\n";
                    }
                }

                //Debug.Log(s);

                if (generics.Count == 0)
                {
                    Debug.Log(Mod.Identifier + "No suitable generics found for " + category + "/" + features);
                    generics.Add(uint.MaxValue, "STREET_NAME_GENERIC_PART_MISSING");
                }
            }

            private static int BinarySearch<T>(IList<T> list, T value)
            {
                var comp = Comparer<T>.Default;
                int lo = 0, hi = list.Count - 1;
                while (lo < hi)
                {
                    int m = (hi + lo) / 2;
                    if (comp.Compare(list[m], value) < 0) lo = m + 1;
                    else hi = m - 1;
                }
                if (comp.Compare(list[lo], value) < 0) lo++;
                return lo;
            }

            public string GetRandomGenericPart(ref Randomizer randomiser)
            {
                var v = randomiser.UInt32(generics.Last().Key);
                int index = BinarySearch(generics.Keys, v);
                return generics[generics.Keys[index]];
            }

            SortedList<uint, string> generics;
        }

        enum GrammaticalGender
        {
            Masculine,
            Feminine
        }

        static private GrammaticalGender? GrammaticalGenderFromString(string s)
        {
            switch (s)
            {
                case "M":
                    return GrammaticalGender.Masculine;
                case "F":
                    return GrammaticalGender.Feminine;
            }

            return null;
        }


        /// <summary>
        /// Properties of the generic part of a street name (e.g. 'Rue ...')
        /// </summary>
        class GenericPartProperties
        {
            public GenericPartProperties(GrammaticalGender gender,
                                         List<RoadElevation> allowedElevations,
                                         Dictionary<RoadCategory, double> categoryFrequencies,
                                         Dictionary<RoadFeature, double> featureModifiers,
                                         Dictionary<RoadFeature, double> negativeFeatureModifiers)
            {
                this.gender = gender;
                this.allowedElevations = allowedElevations;
                this.categoryFrequencies = categoryFrequencies;
                this.featureModifiers = featureModifiers;
                this.negativeFeatureModifiers = negativeFeatureModifiers;
            }

            public GrammaticalGender gender;
            List<RoadElevation> allowedElevations;
            Dictionary<RoadCategory, double> categoryFrequencies;
            Dictionary<RoadFeature, double> featureModifiers;
            Dictionary<RoadFeature, double> negativeFeatureModifiers;

            public uint GetProbability(RoadElevation elevation, RoadCategory category, RoadFeature features)
            {
                if (!allowedElevations.Contains(elevation) ||
                    !categoryFrequencies.ContainsKey(category))
                {
                    return 0;
                }

                double probability = categoryFrequencies[category];

                foreach (var modifier in featureModifiers)
                {
                    if ((modifier.Key & features) != 0)
                    {
                        probability *= modifier.Value;
                    }
                }

                foreach (var modifier in negativeFeatureModifiers)
                {
                    if ((modifier.Key & features) == 0)
                    {
                        probability *= modifier.Value;
                    }
                }

                return (uint)Math.Floor(probability);
            }
        }

        /// <summary>
        /// Properties of the specific part of a street name (e.g. '... de l'Eglise')
        /// </summary>
        class SpecificPartProperties
        {
            public SpecificPartProperties(uint probability,
                                          bool useSpecificPartOnly,
                                          RoadCategory allowedCategories = RoadCategory.ALL,
                                          RoadElevation allowedElevations = RoadElevation.ALL,
                                          RoadFeature forbiddenFeatures = RoadFeature.NONE,
                                          RoadFeature requiredFeatures = RoadFeature.NONE)
            {
                this.probability = probability;
                this.forbiddenFeatures = forbiddenFeatures;
                this.requiredFeatures = requiredFeatures;
                this.allowedCategories = allowedCategories;
                this.allowedElevations = allowedElevations;
                this.useSpecificPartOnly = useSpecificPartOnly;
            }

            public uint probability;
            RoadFeature forbiddenFeatures;
            RoadFeature requiredFeatures;
            RoadCategory allowedCategories;
            RoadElevation allowedElevations;
            bool useSpecificPartOnly;

            public bool IsValidFor(RoadElevation elevation, RoadCategory category, RoadFeature features)
            {
                if (useSpecificPartOnly && (elevation == RoadElevation.BRIDGE || elevation == RoadElevation.TUNNEL))
                {
                    return false;
                }

                return (features & forbiddenFeatures) == 0 &&
                       (features & requiredFeatures) == requiredFeatures &&
                       (allowedCategories & category) == category &&
                       (allowedElevations & elevation) == elevation;
            }
        }

        private static List<string> GenerateSpecificsList(Dictionary<string, SpecificPartProperties> entries)
        {
            var list = new List<string>();

            foreach (var entry in entries)
            {
                for (int i = 0; i < entry.Value.probability; ++i)
                {
                    list.Add(entry.Key);
                }
            }

            return list;
        }

        private static Dictionary<RoadElevation, Dictionary<RoadCategory, Dictionary<RoadFeature, GenericPartList>>> InitGenericsLists()
        {
            var d = new Dictionary<RoadElevation, Dictionary<RoadCategory, Dictionary<RoadFeature, GenericPartList>>>();

            foreach (RoadElevation elev in Enum.GetValues(typeof(RoadElevation)))
            {
                if (elev == RoadElevation.NONE || elev == RoadElevation.ALL)
                {
                    continue;
                }

                foreach (RoadCategory cat in Enum.GetValues(typeof(RoadCategory)))
                {
                    if (cat == RoadCategory.NONE || cat == RoadCategory.ALL)
                    {
                        continue;
                    }

                    if (!d.ContainsKey(elev))
                    {
                        d[elev] = new Dictionary<RoadCategory, Dictionary<RoadFeature, GenericPartList>>();
                    }

                    d[elev][cat] = new Dictionary<RoadFeature, GenericPartList>();
                }
            }

            return d;
        }

        private static readonly Dictionary<string, GenericPartProperties> ROAD_NAME_GENERICS_DATA = LoadGenericsFromCsv("generics.csv");
        private static readonly Dictionary<RoadElevation, Dictionary<RoadCategory, Dictionary<RoadFeature, GenericPartList>>> ROAD_NAME_GENERICS = InitGenericsLists();

        private static readonly Dictionary<string, SpecificPartProperties> ROAD_NAME_SPECIFICS_DATA = LoadSpecificFromCsv("specifics.csv");
        private static readonly List<string> ROAD_NAME_SPECIFICS = GenerateSpecificsList(ROAD_NAME_SPECIFICS_DATA);

        private static RoadFeature RoadFeatureFromString(string s)
        {
            switch (s)
            {
                case "DEADEND":
                    return RoadFeature.DEADEND;
                case "NEAR_LOOP":
                    return RoadFeature.NEAR_LOOP;
                case "LOOP":
                    return RoadFeature.LOOP;
                case "VALLEY":
                    return RoadFeature.VALLEY;
                case "WATERFRONT":
                    return RoadFeature.WATERFRONT;
                case "NEAR_WATER":
                    return RoadFeature.NEAR_WATER;
                case "CROSSES_BRIDGE":
                    return RoadFeature.CROSSES_BRIDGE;
                case "CROSSES_TUNNEL":
                    return RoadFeature.CROSSES_TUNNEL;
                case "CROSSES_WATER":
                    return RoadFeature.CROSSES_WATER;
                case "SHORT":
                    return RoadFeature.SHORT;
                case "LONG":
                    return RoadFeature.LONG;
                case "ONE_WAY":
                    return RoadFeature.ONE_WAY;
                case "STEEP":
                    return RoadFeature.STEEP;
                case "SPARSE_INTERSECTIONS":
                    return RoadFeature.SPARSE_INTERSECTIONS;

                //--- SPECIAL ENTRIES
                case "LOOP_OR_NEAR_LOOP":
                    return RoadFeature.NEAR_LOOP | RoadFeature.LOOP;
            }

            return RoadFeature.NONE;
        }

        private static RoadElevation RoadElevationFromString(string s)
        {
            switch (s)
            {
                case "GROUND":
                    return RoadElevation.GROUND;
                case "BRIDGE":
                    return RoadElevation.BRIDGE;
                case "TUNNEL":
                    return RoadElevation.TUNNEL;
            }

            return RoadElevation.NONE;
        }

        private static RoadCategory RoadCategoryFromString(string s)
        {
            switch (s)
            {
                case "MINOR_PEDESTRIAN":
                    return RoadCategory.MINOR_PEDESTRIAN;
                case "MEDIUM_PEDESTRIAN":
                    return RoadCategory.MEDIUM_PEDESTRIAN;
                case "MAJOR_PEDESTRIAN":
                    return RoadCategory.MAJOR_PEDESTRIAN;
                case "MINOR_RURAL":
                    return RoadCategory.MINOR_RURAL;
                case "MEDIUM_RURAL":
                    return RoadCategory.MEDIUM_RURAL;
                case "MAJOR_RURAL":
                    return RoadCategory.MAJOR_RURAL;
                case "MINOR_URBAN":
                    return RoadCategory.MINOR_URBAN;
                case "MEDIUM_URBAN":
                    return RoadCategory.MEDIUM_URBAN;
                case "MAJOR_URBAN":
                    return RoadCategory.MAJOR_URBAN;
                case "MOTORWAY":
                    return RoadCategory.MOTORWAY;
                case "SQUARE":
                    return RoadCategory.SQUARE;
                case "CIRCLE":
                    return RoadCategory.CIRCLE;
                case "OVAL":
                    return RoadCategory.OVAL;
            }

            return RoadCategory.NONE;
        }

        private static Dictionary<string, GenericPartProperties> LoadGenericsFromCsv(string filename)
        {
            string csvFile = Path.Combine(Mod.GetModDirectory(), filename);
            var generics = new Dictionary<string, GenericPartProperties>();

            if (!File.Exists(csvFile))
            {
                Debug.Log(Mod.Identifier + "List of generics not found at: " + csvFile);
                return generics;
            }

            string line;

            // Read the file and display it line by line. 
            StreamReader file = new StreamReader(csvFile);
            while ((line = file.ReadLine()) != null)
            {
                if (line.StartsWith("#") || line.Length == 0)
                {
                    // '#' marks a comment
                    continue;
                }

                var splitLine = line.Split(',');

                string name = "";
                var allowedElevations = new List<RoadElevation>();
                var categoryFrequencies = new Dictionary<RoadCategory, double>();
                var featureModifiers = new Dictionary<RoadFeature, double>();
                var negativeFeatureModifiers = new Dictionary<RoadFeature, double>();
                GrammaticalGender? gender = null;

                try
                {
                    name = splitLine[0];

                    for (int i = 1; i < splitLine.Length; ++i)
                    {
                        if (gender == null)
                        {
                            gender = GrammaticalGenderFromString(splitLine[i]);
                            if (gender != null)
                            {
                                continue;
                            }
                        }

                        // Check for elevations, these should not have a frequency multiplier, e.g. "GROUND"
                        RoadElevation elevation = RoadElevationFromString(splitLine[i]);
                        if (elevation != RoadElevation.NONE)
                        {
                            allowedElevations.Add(elevation);
                            continue;
                        }

                        // Check for categories (these should be followed by a base frequency, e.g. "MINOR_PEDESTRIAN:100")
                        // and features (these should have a frequency multiplier, e.g. "MINOR_PEDESTRIAN:1.5")

                        var entry = splitLine[i].Split(':');
                        var s = entry[0];
                        double freq = Math.Max(0.0, double.Parse(entry[1]));

                        RoadCategory category = RoadCategoryFromString(s);

                        if (category != RoadCategory.NONE)
                        {
                            categoryFrequencies.Add(category, freq);
                            continue;
                        }

                        bool invert = false;
                        if (s.StartsWith("NOT_"))
                        {
                            invert = true;
                            s = s.Replace("NOT_", "");
                        }

                        RoadFeature feature = RoadFeatureFromString(s);
                        if (feature != RoadFeature.NONE)
                        {
                            if (invert)
                            {
                                negativeFeatureModifiers.Add(feature, freq);
                            }
                            else
                            {
                                featureModifiers.Add(feature, freq);
                            }
                            continue;
                        }

                        Debug.Log(Mod.Identifier + "Reading generics from CSV. Entry: " + splitLine[i] + " (line: " + line + ") not recognised.");
                    }
                }
                catch
                {
                    Debug.Log(Mod.Identifier + "Reading generics from CSV. Exception caught when processing: " + line);
                    continue;
                }

                if (categoryFrequencies.Count == 0)
                {
                    Debug.Log(Mod.Identifier + "No road categories specified: " + line);
                }

                if (allowedElevations.Count == 0)
                {
                    Debug.Log(Mod.Identifier + "No road elevations specified: " + line);
                }

                if (gender == null)
                {
                    Debug.Log(Mod.Identifier + "No grammatical gender specified: " + line);
                    gender = GrammaticalGender.Feminine;
                }

                generics.Add(name, new GenericPartProperties(gender.Value, allowedElevations, categoryFrequencies, featureModifiers, negativeFeatureModifiers));
            }

            file.Close();

            return generics;
        }

        private static Dictionary<string, SpecificPartProperties> LoadSpecificFromCsv(string filename)
        {
            string csvFile = Path.Combine(Mod.GetModDirectory(), filename);
            var specifics = new Dictionary<string, SpecificPartProperties>();

            if (!File.Exists(csvFile))
            {
                Debug.Log(Mod.Identifier + "List of specifics not found at: " + csvFile);
                specifics.Add("STREET_NAME_SPECIFIC_MISSING", new SpecificPartProperties(1, true));
                return specifics;
            }

            string line;

            // Read the file and display it line by line. 
            StreamReader file = new StreamReader(csvFile);
            while ((line = file.ReadLine()) != null)
            {
                if (line.StartsWith("#") || line.Length == 0)
                {
                    // '#' marks a comment
                    continue;
                }

                var splitLine = line.Split(',');

                string name = "";
                uint probability = 0;
                var allowedElevations = RoadElevation.ALL;
                var allowedCategories = RoadCategory.NONE;
                var requiredFeatures = RoadFeature.NONE;
                var forbiddenFeatures = RoadFeature.NONE;

                try
                {
                    name = splitLine[0];
                    probability = uint.Parse(splitLine[1]);

                    for (int i = 2; i < splitLine.Length; ++i)
                    {
                        var s = splitLine[i];

                        RoadCategory category = RoadCategoryFromString(s);

                        if (category != RoadCategory.NONE)
                        {
                            allowedCategories |= category;
                            continue;
                        }

                        bool invert = false;
                        if (s.StartsWith("NOT_"))
                        {
                            invert = true;
                            s = s.Replace("NOT_", "");
                        }

                        RoadElevation elevation = RoadElevationFromString(s);

                        if (elevation != RoadElevation.NONE)
                        {
                            if (invert)
                            {
                                allowedElevations &= ~elevation;
                            }
                            // ALL allowed by default, so only NOT-s processed
                            continue;
                        }

                        RoadFeature feature = RoadFeatureFromString(s);
                        if (feature != RoadFeature.NONE)
                        {
                            if (invert)
                            {
                                forbiddenFeatures |= feature;
                            }
                            else
                            {
                                requiredFeatures |= feature;
                            }
                            continue;
                        }

                        Debug.Log(Mod.Identifier + "Reading specifics from CSV. Entry: " + splitLine[i] + " (line: " + line + ") not recognised.");
                    }
                }
                catch
                {
                    Debug.Log(Mod.Identifier + "Reading specifics from CSV. Exception caught when processing: " + line);
                    continue;
                }

                if (allowedCategories == RoadCategory.NONE)
                {
                    allowedCategories = RoadCategory.ALL;
                }

                bool useSpecificPartOnly = name.EndsWith("$");
                specifics.Add(name, new SpecificPartProperties(probability, useSpecificPartOnly,
                                                               allowedCategories, allowedElevations,
                                                               forbiddenFeatures, requiredFeatures));
            }

            file.Close();

            return specifics;
        }
    }
}
