﻿using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

namespace WalkerSim
{
    [XmlRoot("WalkerSim", Namespace = "http://zeh.matt/WalkerSimSchema")]
    [XmlTypeAttribute(AnonymousType = true, Namespace = "http://zeh.matt/WalkerSimSchema")]
    public class Config
    {
        public enum WorldLocation
        {
            [XmlEnum("None")]
            None = 0,
            [XmlEnum("RandomBorderLocation")]
            RandomBorderLocation,
            [XmlEnum("RandomLocation")]
            RandomLocation,
            [XmlEnum("RandomPOI")]
            RandomPOI,
            [XmlEnum("Mixed")]
            Mixed,
        }

        public enum MovementProcessorType
        {
            Invalid = 0,
            [XmlEnum("FlockAnyGroup")]
            FlockAnyGroup,
            [XmlEnum("AlignAnyGroup")]
            AlignAnyGroup,
            [XmlEnum("AvoidAnyGroup")]
            AvoidAnyGroup,
            [XmlEnum("FlockSameGroup")]
            FlockSameGroup,
            [XmlEnum("AlignSameGroup")]
            AlignSameGroup,
            [XmlEnum("AvoidSameGroup")]
            AvoidSameGroup,
            [XmlEnum("FlockOtherGroup")]
            FlockOtherGroup,
            [XmlEnum("AlignOtherGroup")]
            AlignOtherGroup,
            [XmlEnum("AvoidOtherGroup")]
            AvoidOtherGroup,
            [XmlEnum("Wind")]
            Wind,
            [XmlEnum("WindInverted")]
            WindInverted,
            [XmlEnum("StickToRoads")]
            StickToRoads,
            [XmlEnum("AvoidRoads")]
            AvoidRoads,
            [XmlEnum("StickToPOIs")]
            StickToPOIs,
            [XmlEnum("AvoidPOIs")]
            AvoidPOIs,
            [XmlEnum("WorldEvents")]
            WorldEvents,
        }

        public class MovementProcessor
        {
            [XmlAttribute("Type")]
            public MovementProcessorType Type;

            [XmlAttribute("Distance")]
            public float Distance = 0.0f;

            [XmlAttribute("Power")]
            public float Power = 0.0f;
        }

        public class MovementProcessors
        {
            [XmlAttribute("Group")]
            public int Group = -1;

            [XmlAttribute("SpeedScale")]
            public float SpeedScale = 1.0f;

            [XmlAttribute("Color")]
            public string Color = "";

            [XmlElement("Processor")]
            public List<MovementProcessor> Entries = new List<MovementProcessor>();
        }

        public class Biome
        {
            [XmlAttribute("Name")]
            public string Name;

            [XmlAttribute("PopulationDensity")]
            public int PopulationDensity;

            [XmlAttribute("FallbackSpawnGroup")]
            public string FallbackSpawnGroup;
        }

        public class DebugOptions
        {
            [XmlElement("LogSpawnDespawn")]
            public bool LogSpawnDespawn;
        }

        [XmlElement("DebugOptions")]
        public DebugOptions Debug;

        [XmlElement("RandomSeed")]
        public int RandomSeed = 1337;

        [XmlElement("PopulationDensity")]
        public int PopulationDensity = 160;

        [XmlElement("StartAgentsGrouped")]
        public bool StartAgentsGrouped = true;

        [XmlElement("GroupSize")]
        public int GroupSize = 200;

        [XmlElement("AgentStartPosition")]
        public WorldLocation StartPosition = WorldLocation.RandomLocation;

        [XmlElement("AgentRespawnPosition")]
        public WorldLocation RespawnPosition = WorldLocation.None;

        [XmlElement("FallbackSpawnGroup")]
        public string FallbackSpawnGroup = "ZombiesAll";

        [XmlElement("PauseDuringBloodmoon")]
        public bool PauseDuringBloodmoon = false;

        [XmlElement("MovementProcessors")]
        public List<MovementProcessors> Processors;

        [XmlElement("Biome")]
        public List<Biome> Biomes;

        private static void SanitizeConfig(Config config)
        {
            if (config.PopulationDensity is < Simulation.Limits.MinDensity
                    or > Simulation.Limits.MaxDensity)
            {
                Logging.Warn("Invalid value for PopulationDensity (Min: {0}, Max: {1}), clamping.",
                    Simulation.Limits.MinDensity,
                    Simulation.Limits.MaxDensity);

                config.PopulationDensity = Math.Clamp(config.PopulationDensity,
                    Simulation.Limits.MinDensity,
                    Simulation.Limits.MaxDensity);
            }

            foreach(var biome in config.Biomes){
                if(biome.PopulationDensity is >= Simulation.Limits.MinDensity
                        and <= Simulation.Limits.MaxDensity){
                    continue;
                }

                Logging.Warn($"Invalid Density value '{biome.PopulationDensity}' in BiomePopulationDensity with Biome '{biome.Name}', clamping.");

                biome.PopulationDensity = Math.Clamp(biome.PopulationDensity,
                    Simulation.Limits.MinDensity,
                    Simulation.Limits.MaxDensity);
            }
        }

        public static Config LoadFromFile(string filePath)
        {
            var serializer = new XmlSerializer(typeof(Config));
            try
            {
                using (var reader = new System.IO.StreamReader(filePath))
                {
                    var config = (Config)serializer.Deserialize(reader);
                    if (config == null)
                        return null;

                    SanitizeConfig(config);
                    return config;
                }
            }
            catch (System.Exception ex)
            {
                Logging.Exception(ex);
                return null;
            }
        }

        public static Config GetDefault()
        {
            var conf = new Config()
            {
                RandomSeed = 1337,
                PopulationDensity = 160,
                GroupSize = 32,
                StartPosition = WorldLocation.RandomLocation,
                RespawnPosition = WorldLocation.RandomBorderLocation,
                StartAgentsGrouped = true,
                Processors = new List<MovementProcessors>
                {
                    new MovementProcessors {
                        Group = -1,
                        SpeedScale = 1.0f,
                        Entries = new List<MovementProcessor> {
                            new MovementProcessor()
                            {
                                Type = MovementProcessorType.FlockAnyGroup,
                                Distance = 50f,
                                Power = 0.0001f,
                            },
                            new MovementProcessor()
                            {
                                Type = MovementProcessorType.AlignAnyGroup,
                                Distance = 50f,
                                Power = 0.0001f,
                            },
                            new MovementProcessor()
                            {
                                Type = MovementProcessorType.AvoidAnyGroup,
                                Distance = 30f,
                                Power = 0.0002f,
                            },
                            new MovementProcessor()
                            {
                                Type = MovementProcessorType.Wind,
                                Distance = 0f,
                                Power = 0.0001f,
                            },
                            new MovementProcessor()
                            {
                                Type = MovementProcessorType.StickToRoads,
                                Distance = 0f,
                                Power = 0.0025f,
                            },
                            new MovementProcessor()
                            {
                                Type = MovementProcessorType.WorldEvents,
                                Distance = 0f,
                                Power = 0.0050f,
                            },
                        }
                    },
                },
                Debug = new DebugOptions(),
            };
            return conf;
        }

        public void Export(TextWriter writer)
        {
            var serializer = new XmlSerializer(typeof(Config));
            try
            {
                serializer.Serialize(writer, this);
            }
            catch (System.Exception)
            {
                throw;
            }
        }
    }
}
