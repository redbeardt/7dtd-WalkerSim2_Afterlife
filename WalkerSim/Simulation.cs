using System;
using System.Diagnostics;
using System.Threading;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

namespace WalkerSim;

internal partial class Simulation
{
    public static Simulation Instance = new();

    public float TimeScale = 1.0f;

    public const int TicksPerSecond = 40;
    public const float TickRate = 1f / TicksPerSecond;
    public const int TickRateMs = 1000 / TicksPerSecond;

    private Thread _thread;
    private bool _running = false;
    private bool _shouldStop = false;
    private bool _paused = false;
    private bool _fastAdvanceAtStart = false;
    private readonly Dictionary<string, float> _biomeSpawnProbs = [];

    private Vector3[] _groupStarts = [];

    public void Stop()
    {
        if (_thread == null)
        {
            return;
        }

        _shouldStop = true;
        _thread.Join();
        _thread = null;
        _running = false;
        _nextAutoSave = DateTime.MaxValue;

        Logging.Out("Simulation stopped.");
    }

    public void Advance(uint numTicks)
    {
        if (_running)
            throw new Exception("Can't advance the simulation while its running");

        for (uint i = 0; i < numTicks; i++)
        {
            Tick();
        }
    }

    public void Start()
    {
        Stop();

        if (_autoSaveInterval != -1)
        {
            _nextAutoSave = DateTime.Now.AddSeconds(_autoSaveInterval);
        }

        _running = true;
        _shouldStop = false;
        _thread = new Thread(ThreadUpdate);
        _thread.Start();

        Logging.Out("Started Simulation.");
    }

    public void SetWorldSize(Vector3 worldMins, Vector3 worldMaxs)
    {
        lock (_state)
        {
            _state.WorldMins = worldMins;
            _state.WorldMaxs = worldMaxs;

            UpdateGrid();
        }
    }

    public void Reset(Config config)
    {
        Stop();

        lock (_state)
        {
            _state.Config = config;
            _state.PRNG = new(config.RandomSeed);
            _state.SlowIterator = 0;
            _state.TickNextWindChange = 0;
            _state.Ticks = 0;
            _state.POIIterator = 0;

            SetupGrid();
            Populate();
            SetupProcessors();
        }
    }

    public void SetPaused(bool paused)
    {
        if (_paused != paused)
        {
            if (paused)
                Logging.Out("Paused simulation.");
            else
                Logging.Out("Resuming simulation.");
        }
        _paused = paused;
    }

    public void SetFastAdvanceAtStart(bool fastAdvance)
    {
        _fastAdvanceAtStart = fastAdvance;
    }

    public void EntityKilled(int entityId)
    {
        if (_state.Active.TryGetValue(entityId, out var agent))
        {
            MarkAgentDead(agent);
        }
    }

    public bool LoadMapData(string directoryPath)
    {
        lock (_state)
        {
            var mapData = MapData.LoadFromFolder(directoryPath);
            if (mapData == null)
                return false;

            _state.MapData = mapData;

            if (WorldSize != mapData.WorldSize)
            {
                SetWorldSize(mapData.WorldMins, mapData.WorldMaxs);
            }

            return true;
        }
    }

    private Vector3 GetRandomPosition()
    {
        Random prng = _state.PRNG;
        float borderSize = 250;
        float x0, y0, x, y;

        if (_state.Config.Biomes.Count > 0)
        {
            for (int i = 0; i < 256; i++)
            {
                x0 = (float)prng.NextDouble();
                y0 = (float)prng.NextDouble();
                x = Math.Remap(x0, 0f, 1f, _state.WorldMins.X + borderSize, _state.WorldMaxs.X - borderSize);
                y = Math.Remap(y0, 0f, 1f, _state.WorldMins.Y + borderSize, _state.WorldMaxs.Y - borderSize);
                BiomeDefinition biome = Utils.GetBiomeAt((int)x, (int)y);

                if (_biomeSpawnProbs.TryGetValue(biome.m_sBiomeName, out float prob)
                        && prng.NextSingle() < prob)
                {
                    return new(x, y);
                }
            }
        }

        x0 = (float)prng.NextDouble();
        y0 = (float)prng.NextDouble();
        x = Math.Remap(x0, 0f, 1f, _state.WorldMins.X + borderSize, _state.WorldMaxs.X - borderSize);
        y = Math.Remap(y0, 0f, 1f, _state.WorldMins.Y + borderSize, _state.WorldMaxs.Y - borderSize);
        return new(x, y);
    }

    private Vector3 GetRandomBorderPosition()
    {
        Vector3 res = new();

        float borderSize = 250;
        var prng = _state.PRNG;
        var worldMins = _state.WorldMins;
        var worldMaxs = _state.WorldMaxs;

        // Select border side.
        int side = prng.Next(0, 4);
        if (side == 0)
        {
            // Top.
            float x0 = (float)prng.NextDouble();
            res.X = Math.Remap(x0, 0f, 1f, worldMins.X + borderSize, worldMaxs.X - borderSize);
            res.Y = worldMins.Y + borderSize;
        }
        else if (side == 1)
        {
            // Right.
            res.X = worldMaxs.X - borderSize;
            float y0 = (float)prng.NextDouble();
            res.Y = Math.Remap(y0, 0f, 1f, worldMins.Y + borderSize, worldMaxs.Y - borderSize);
        }
        else if (side == 2)
        {
            // Bottom.
            float x0 = (float)prng.NextDouble();
            res.X = Math.Remap(x0, 0f, 1f, worldMins.X + borderSize, worldMaxs.X - borderSize);
            res.Y = worldMaxs.Y - borderSize;
        }
        else if (side == 3)
        {
            // Left.
            res.X = worldMins.X + borderSize;
            float y0 = (float)prng.NextDouble();
            res.Y = Math.Remap(y0, 0f, 1f, worldMins.Y + borderSize, worldMaxs.Y - borderSize);
        }

        return res;
    }

    Vector3 GetRandomPOIPosition()
    {
        if (_state.Config.Biomes.Any(a => a.PopulationDensity != _state.Config.PopulationDensity))
        {
            Vector3 biomeAwarePos = GetRandomPOIPositionBiomeAware();

            if (biomeAwarePos != Vector3.Zero){
                return biomeAwarePos;
            }
        }

        var mapData = _state.MapData;
        if (mapData == null)
        {
            // Can be null in viewer.
            return GetRandomBorderPosition();
        }

        var prefabs = mapData.Prefabs;
        var decos = prefabs.Decorations;

        //var prng = _state.PRNG;
        //var selectedIdx = prng.Next(decos.Length);
        var selectedIdx = _state.POIIterator % decos.Length;
        _state.POIIterator += 7;

        return decos[selectedIdx].Position;
    }

    private Vector3 GetRandomPOIPositionBiomeAware()
    {
        var mapData = _state.MapData;
        if (mapData == null || GameManager.Instance.World is not { } world)
        {
            // Can be null in viewer.
            return GetRandomBorderPosition();
        }

        Random prng = _state.PRNG;
        DynamicPrefabDecorator decorator = GameManager.Instance.GetDynamicPrefabDecorator();
        List<PrefabInstance> prefabs = decorator.allPrefabs;
        float biomeProb = prng.NextSingle();
        string[] names = [.. _biomeSpawnProbs.Keys];
        float[] probs = [.. _biomeSpawnProbs.Values];
        float highestProb = 0f;
        string targetBiome = "";

        for (int i = 0; i < names.Length; i++)
        {
            if (biomeProb > probs[i] && probs[i] > highestProb)
            {
                targetBiome = names[i];
                highestProb = probs[i];
            }
        }

        for (int i = 0; i < 256; i++)
        {
            PrefabInstance prefabInst = prefabs[prng.Next() % prefabs.Count];
            Vector3i prefabPos = prefabInst.boundingBoxPosition;
            Vector3 pos = new(prefabPos.x, prefabPos.y, prefabPos.z);
            BiomeDefinition biome = Utils.GetBiomeAt((int)pos.X, (int)-pos.Z);

            if (biome.m_sBiomeName == targetBiome)
            {
                return new(pos.X, -pos.Z);
            }
        }

        return Vector3.Zero;
    }

    Vector3 GetGroupPosition(int groupIndex)
    {
        return _groupStarts[groupIndex];
    }

    Vector3 GetWorldLocation(Config.WorldLocation worldLoc)
    {
        var prng = _state.PRNG;

        if (worldLoc == Config.WorldLocation.Mixed)
        {
            var min = Config.WorldLocation.RandomBorderLocation;
            var max = Config.WorldLocation.RandomPOI;
            worldLoc = (Config.WorldLocation)prng.Next((int)min, (int)max + 1);
        }

        switch (worldLoc)
        {
            case Config.WorldLocation.None:
                break;
            case Config.WorldLocation.RandomBorderLocation:
                return GetRandomBorderPosition();
            case Config.WorldLocation.RandomLocation:
                return GetRandomPosition();
            case Config.WorldLocation.RandomPOI:
                return GetRandomPOIPosition();
        }

        // This should never happen.
        throw new Exception("Bad starting location type");
    }

    Vector3 GetStartLocation()
    {
        var config = _state.Config;
        return GetWorldLocation(config.StartPosition);
    }

    Vector3 GetRespawnLocation()
    {
        var config = _state.Config;
        return GetWorldLocation(config.RespawnPosition);
    }

    Vector3 GetStartLocation(int index, int groupIndex)
    {
        var config = _state.Config;

        // Give each agent 2 meters distance to each other.
        var maxDistance = Math.Clamp(_state.Config.GroupSize * 2.0f, 10.0f, 500.0f);

        if (config.StartAgentsGrouped)
        {
            // Spawn in circle.
            float angle = (float)_state.PRNG.NextDouble() * (float)System.Math.PI * 2.0f;
            float radius = (float)_state.PRNG.NextDouble() * maxDistance;
            float offsetX = (float)System.Math.Cos(angle) * radius;
            float offsetY = (float)System.Math.Sin(angle) * radius;

            return _groupStarts[groupIndex] + new Vector3(offsetX, offsetY);
        }
        else
        {
            return GetStartLocation();
        }
    }

    private void Populate()
    {
        Utils.ClearCachedData();
        _biomeSpawnProbs.Clear();
        var agents = _state.Agents;
        var config = _state.Config;
        var prng = _state.PRNG;

        agents.Clear();

        float numSqrKm = WorldSize.X / 1000.0f * (WorldSize.Y / 1000.0f);
        World world = GameManager.Instance.World;
        bool useBiomePopDensities = config.Biomes.Count > 0;
        Dictionary<string, int> biomeAgentCounts = [];
        int maxAgents = 0;

        if (useBiomePopDensities)
        {
            Dictionary<string, float> biomeProportions = Utils.GetBiomeProportions();

            foreach (string biomeName in world.Biomes.m_Name2BiomeMap.Keys)
            {
                float biomeDensityRelative = 1f;
                float biomeProportion = 1f;

                if (biomeProportions.FirstOrDefault(a => a.Key == biomeName) is { } biomeProportionsValue)
                {
                    biomeProportion = biomeProportionsValue.Value;
                }

                if (config.Biomes.FirstOrDefault(a => a.Name == biomeName) is { } biomeDensitiesValue)
                {
                    biomeDensityRelative = (float)biomeDensitiesValue.PopulationDensity / config.PopulationDensity;
                }

                _biomeSpawnProbs[biomeName] = biomeDensityRelative * biomeProportion;
                maxAgents += (int)Math.Ceiling(numSqrKm * biomeDensityRelative * biomeProportion * config.PopulationDensity);
            }
        }
        else
        {
            maxAgents = (int)System.Math.Ceiling(numSqrKm * config.PopulationDensity);
        }

        // Normalise biome spawn probabilities.
        float biomeSpawnProbSum = _biomeSpawnProbs.Sum(a => a.Value);

        foreach (string biomeName in world.Biomes.GetBiomeMap().Select(static a => a.Value.m_sBiomeName))
        {
            _biomeSpawnProbs[biomeName] /= biomeSpawnProbSum;
        }

        maxAgents = Math.Clamp(maxAgents, 1, Limits.MaxAgents);
        _state.GroupCount = maxAgents / config.GroupSize;

        if (maxAgents % config.GroupSize != 0)
        {
            _state.GroupCount++;
        }

        _groupStarts = new Vector3[_state.GroupCount];

        for (int i = 0; i < _groupStarts.Length; i++)
        {
            _groupStarts[i] = GetStartLocation();
        }

        for (int index = 0; index < maxAgents; index++)
        {
            int groupIndex = index / config.GroupSize;

            Agent agent = new(index, groupIndex)
            {
                LastUpdateTick = _state.Ticks,
                Position = GetStartLocation(index, groupIndex)
            };

            // Ensure the position is not out of bounds.
            Warp(agent);

            agent.Velocity.X = (float)(prng.NextDouble() * 3f);
            agent.Velocity.Y = (float)(prng.NextDouble() * 3f);

            agents.Add(agent);

            MoveInGrid(agent);
        }
    }

    private void ThreadUpdate()
    {
        Stopwatch sw = new();
        sw.Start();

        if (_fastAdvanceAtStart)
        {
            Logging.Out("Advancing simulation for {0} ticks...", Limits.TicksToAdvanceOnStartup);

            var elapsed = Utils.Measure(() =>
            {
                var oldTimeScale = TimeScale;
                TimeScale = 64.0f;
                for (uint num = 0u; num < Limits.TicksToAdvanceOnStartup && !_shouldStop; num++)
                {
                    Tick();
                }
                TimeScale = oldTimeScale;
            });

            Logging.Out("... done, took {0}.", elapsed);
        }

        while (!_shouldStop)
        {
            if (_paused)
            {
                Thread.Sleep(TickRateMs);
                continue;
            }

            sw.Restart();

            {
                Tick();

                if (_shouldStop)
                    break;

                CheckAgentSpawn();
                CheckAutoSave();
            }

            sw.Stop();
            var elapsedMs = tickWatch.Elapsed.TotalMilliseconds;

            lastTickTimeMs = (float)elapsedMs;
            averageTickTime += (float)elapsedMs;

            if (_state.Ticks > 1)
                averageTickTime *= 0.5f;

            if (_shouldStop)
                break;

            var sleepTime = Math.Clamp((int)(elapsedMs - TickRateMs), 0, TickRateMs);
            Thread.Sleep(sleepTime);
        }

        _running = false;
        _shouldStop = false;
    }

    // Called from the main thread, this should be invoked from GameUpdate.
    public void GameUpdate(float deltaTime)
    {
        if (!_running || _shouldStop || _paused)
        {
            return;
        }

        ProcessSpawnQueue();
        CheckAgentDespawn();
    }

    public Vector3 RemapPosition2D(Vector3 pos, Vector3 min, Vector3 max)
    {
        var worldMins = _state.WorldMins;
        var worldMaxs = _state.WorldMaxs;

        pos.X = Math.Remap(pos.X, worldMins.X, worldMaxs.X, min.X, max.X);
        pos.Y = Math.Remap(pos.Y, worldMins.Y, worldMaxs.Y, min.Y, max.Y);
        pos.Z = 0;

        return pos;
    }

    public void ReloadConfig(Config config)
    {
        lock (_state)
        {
            _state.Config = config;

            SetupProcessors();
        }
    }

    public System.Drawing.Color GetGroupColor(int groupIndex)
    {
        if (groupIndex >= _processors.Count)
        {
            return ColorTable.GetColorForIndex(groupIndex);
        }

        return _processors[groupIndex].Color;
    }
}
