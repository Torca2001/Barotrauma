﻿using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class LevelData
    {
        public enum LevelType
        {
            LocationConnection,
            Outpost
        }

        public readonly LevelType Type;

        public readonly string Seed;

        public readonly float Difficulty;

        public readonly Biome Biome;

        public readonly LevelGenerationParams GenerationParams;

        public bool HasBeaconStation;
        public bool IsBeaconActive;

        public bool HasHuntingGrounds, OriginallyHadHuntingGrounds;

        /// <summary>
        /// Minimum difficulty of the level before hunting grounds can appear.
        /// </summary>
        public const float HuntingGroundsDifficultyThreshold = 25;

        /// <summary>
        /// Probability of hunting grounds appearing in 100% difficulty levels.
        /// </summary>
        public const float MaxHuntingGroundsProbability = 0.3f;

        public OutpostGenerationParams ForceOutpostGenerationParams;

        public bool AllowInvalidOutpost;

        public readonly Point Size;

        public readonly int InitialDepth;

        /// <summary>
        /// Determined during level generation based on the size of the submarine. Null if the level hasn't been generated.
        /// </summary>
        public int? MinMainPathWidth;

        public readonly List<EventPrefab> EventHistory = new List<EventPrefab>();
        public readonly List<EventPrefab> NonRepeatableEvents = new List<EventPrefab>();

        public float CrushDepth
        {
            get
            {
                return Math.Max(Size.Y, Level.DefaultRealWorldCrushDepth / Physics.DisplayToRealWorldRatio) - InitialDepth;
            }
        }
        public float RealWorldCrushDepth
        {
            get
            {
                return Math.Max(Size.Y * Physics.DisplayToRealWorldRatio, Level.DefaultRealWorldCrushDepth);
            }
        }

        public LevelData(string seed, float difficulty, float sizeFactor, LevelGenerationParams generationParams, Biome biome)
        {
            Seed = seed ?? throw new ArgumentException("Seed was null");
            Biome = biome ?? throw new ArgumentException("Biome was null");
            GenerationParams = generationParams ?? throw new ArgumentException("Level generation parameters were null");
            Type = GenerationParams.Type;
            Difficulty = difficulty;

            sizeFactor = MathHelper.Clamp(sizeFactor, 0.0f, 1.0f);
            int width = (int)MathHelper.Lerp(generationParams.MinWidth, generationParams.MaxWidth, sizeFactor);

            InitialDepth = (int)MathHelper.Lerp(generationParams.InitialDepthMin, generationParams.InitialDepthMax, sizeFactor);

            Size = new Point(
                (int)MathUtils.Round(width, Level.GridCellSize),
                (int)MathUtils.Round(generationParams.Height, Level.GridCellSize));
        }

        public LevelData(XElement element, float? forceDifficulty = null)
        {
            Seed = element.GetAttributeString("seed", "");
            Difficulty = forceDifficulty ?? element.GetAttributeFloat("difficulty", 0.0f);
            Size = element.GetAttributePoint("size", new Point(1000));
            Enum.TryParse(element.GetAttributeString("type", "LocationConnection"), out Type);

            HasBeaconStation = element.GetAttributeBool("hasbeaconstation", false);
            IsBeaconActive = element.GetAttributeBool("isbeaconactive", false);

            HasHuntingGrounds = element.GetAttributeBool("hashuntinggrounds", false);
            OriginallyHadHuntingGrounds = element.GetAttributeBool("originallyhadhuntinggrounds", HasHuntingGrounds);

            string generationParamsId = element.GetAttributeString("generationparams", "");
            GenerationParams = LevelGenerationParams.LevelParams.Find(l => l.Identifier == generationParamsId || (!l.OldIdentifier.IsEmpty && l.OldIdentifier == generationParamsId));
            if (GenerationParams == null)
            {
                DebugConsole.ThrowError($"Error while loading a level. Could not find level generation params with the ID \"{generationParamsId}\".");
                GenerationParams = LevelGenerationParams.LevelParams.FirstOrDefault(l => l.Type == Type);
                if (GenerationParams == null)
                {
                    GenerationParams = LevelGenerationParams.LevelParams.First();
                }
            }

            InitialDepth = element.GetAttributeInt("initialdepth", GenerationParams.InitialDepthMin);

            string biomeIdentifier = element.GetAttributeString("biome", "");
            Biome = Biome.Prefabs.FirstOrDefault(b => b.Identifier == biomeIdentifier || (!b.OldIdentifier.IsEmpty && b.OldIdentifier == biomeIdentifier));
            if (Biome == null)
            {
                DebugConsole.ThrowError($"Error in level data: could not find the biome \"{biomeIdentifier}\".");
                Biome = Biome.Prefabs.First();
            }

            string[] prefabNames = element.GetAttributeStringArray("eventhistory", new string[] { });
            EventHistory.AddRange(EventPrefab.Prefabs.Where(p => prefabNames.Any(n => p.Identifier == n)));

            string[] nonRepeatablePrefabNames = element.GetAttributeStringArray("nonrepeatableevents", new string[] { });
            NonRepeatableEvents.AddRange(EventPrefab.Prefabs.Where(p => nonRepeatablePrefabNames.Any(n => p.Identifier == n)));
        }


        /// <summary>
        /// Instantiates level data using the properties of the connection (seed, size, difficulty)
        /// </summary>
        public LevelData(LocationConnection locationConnection)
        {
            Seed = locationConnection.Locations[0].BaseName + locationConnection.Locations[1].BaseName;
            Biome = locationConnection.Biome;
            Type = LevelType.LocationConnection;
            Difficulty = locationConnection.Difficulty;
            GenerationParams = LevelGenerationParams.GetRandom(Seed, LevelType.LocationConnection, Difficulty, Biome.Identifier);

            float sizeFactor = MathUtils.InverseLerp(
                MapGenerationParams.Instance.SmallLevelConnectionLength,
                MapGenerationParams.Instance.LargeLevelConnectionLength,
                locationConnection.Length);
            int width = (int)MathHelper.Lerp(GenerationParams.MinWidth, GenerationParams.MaxWidth, sizeFactor);
            Size = new Point(
                (int)MathUtils.Round(width, Level.GridCellSize),
                (int)MathUtils.Round(GenerationParams.Height, Level.GridCellSize));

            var rand = new MTRandom(ToolBox.StringToInt(Seed));
            InitialDepth = (int)MathHelper.Lerp(GenerationParams.InitialDepthMin, GenerationParams.InitialDepthMax, (float)rand.NextDouble());
            if (Biome.IsEndBiome)
            {
                HasHuntingGrounds = false;
                HasBeaconStation = false;
            }
            else
            {
                HasHuntingGrounds = OriginallyHadHuntingGrounds = rand.NextDouble() < MathUtils.InverseLerp(HuntingGroundsDifficultyThreshold, 100.0f, Difficulty) * MaxHuntingGroundsProbability;
                HasBeaconStation = !HasHuntingGrounds && rand.NextDouble() < locationConnection.Locations.Select(l => l.Type.BeaconStationChance).Max();
            }            
            IsBeaconActive = false;
        }

        /// <summary>
        /// Instantiates level data using the properties of the location
        /// </summary>
        public LevelData(Location location, float difficulty)
        {
            Seed = location.BaseName;
            Biome = location.Biome;
            Type = LevelType.Outpost;
            Difficulty = difficulty;
            GenerationParams = LevelGenerationParams.GetRandom(Seed, LevelType.Outpost, Difficulty, Biome.Identifier);

            var rand = new MTRandom(ToolBox.StringToInt(Seed));
            int width = (int)MathHelper.Lerp(GenerationParams.MinWidth, GenerationParams.MaxWidth, (float)rand.NextDouble());
            InitialDepth = (int)MathHelper.Lerp(GenerationParams.InitialDepthMin, GenerationParams.InitialDepthMax, (float)rand.NextDouble());
            Size = new Point(
                (int)MathUtils.Round(width, Level.GridCellSize),
                (int)MathUtils.Round(GenerationParams.Height, Level.GridCellSize));
        }

        public static LevelData CreateRandom(string seed = "", float? difficulty = null, LevelGenerationParams generationParams = null, bool requireOutpost = false)
        {
            if (string.IsNullOrEmpty(seed))
            {
                seed = Rand.Range(0, int.MaxValue, Rand.RandSync.ServerAndClient).ToString();
            }

            Rand.SetSyncedSeed(ToolBox.StringToInt(seed));

            LevelType type = generationParams == null ?
                (requireOutpost ? LevelType.Outpost : LevelType.LocationConnection) :
                 generationParams.Type;

            float selectedDifficulty = difficulty ?? Rand.Range(30.0f, 80.0f, Rand.RandSync.ServerAndClient);

            if (generationParams == null) { generationParams = LevelGenerationParams.GetRandom(seed, type, selectedDifficulty); }
            var biome =
                Biome.Prefabs.FirstOrDefault(b => generationParams?.AllowedBiomeIdentifiers.Contains(b.Identifier) ?? false) ??
                Biome.Prefabs.GetRandom(Rand.RandSync.ServerAndClient);

            var levelData = new LevelData(
                seed,
                selectedDifficulty,
                Rand.Range(0.0f, 1.0f, Rand.RandSync.ServerAndClient),
                generationParams,
                biome);
            if (type == LevelType.LocationConnection)
            {
                float beaconRng = Rand.Range(0.0f, 1.0f, Rand.RandSync.ServerAndClient);
                levelData.HasBeaconStation = beaconRng < 0.5f;
                levelData.IsBeaconActive = beaconRng > 0.25f;
            }
            if (GameMain.GameSession?.GameMode != null)
            {
                foreach (Mission mission in GameMain.GameSession.GameMode.Missions)
                {
                    mission.AdjustLevelData(levelData);
                }
            }
            return levelData;
        }

        public void Save(XElement parentElement)
        {
            var newElement = new XElement("Level",
                    new XAttribute("seed", Seed),
                    new XAttribute("biome", Biome.Identifier),
                    new XAttribute("type", Type.ToString()),
                    new XAttribute("difficulty", Difficulty.ToString("G", CultureInfo.InvariantCulture)),
                    new XAttribute("size", XMLExtensions.PointToString(Size)),
                    new XAttribute("generationparams", GenerationParams.Identifier),
                    new XAttribute("initialdepth", InitialDepth));

            if (HasBeaconStation)
            {
                newElement.Add(
                    new XAttribute("hasbeaconstation", HasBeaconStation.ToString()),
                    new XAttribute("isbeaconactive", IsBeaconActive.ToString()));
            }

            if (HasHuntingGrounds)
            {
                newElement.Add(
                    new XAttribute("hashuntinggrounds", true));
            }
            if (HasHuntingGrounds || OriginallyHadHuntingGrounds)
            {
                newElement.Add(
                    new XAttribute("originallyhadhuntinggrounds", true));
            }

            if (Type == LevelType.Outpost)
            {
                if (EventHistory.Any())
                {
                    newElement.Add(new XAttribute("eventhistory", string.Join(',', EventHistory.Select(p => p.Identifier))));
                }
                if (NonRepeatableEvents.Any())
                {
                    newElement.Add(new XAttribute("nonrepeatableevents", string.Join(',', NonRepeatableEvents.Select(p => p.Identifier))));
                }
            }
            parentElement.Add(newElement);
        }
    }
}