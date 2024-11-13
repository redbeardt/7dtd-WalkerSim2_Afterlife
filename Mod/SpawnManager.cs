using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using BGSS = WalkerSim.BiomeGSScaling;

namespace WalkerSim;

internal class SpawnManager
{
    private static readonly List<PrefabInstance> spawnPIs = [];
    private static readonly FastTags<TagGroup.Poi> EmptyTag =
        FastTags<TagGroup.Poi>.Parse("empty");

    private static bool CanSpawnZombie()
    {
        // Check for maximum count.
        int alive = GameStats.GetInt(EnumGameStats.EnemyCount);

        // We only allow half of the max count to be spawned to give sleepers some room.
        // TODO: Make this a configuration.
        int maxAllowed = GamePrefs.GetInt(EnumGamePrefs.MaxSpawnedZombies) / 2;

        if (alive >= maxAllowed)
        {
            Logging.Debug("Max zombies reached, alive: {0}, max: {1}", alive, maxAllowed);
            return false;
        }

        return true;
    }

    private static bool CanSpawnAtPosition(UnityEngine.Vector3 position)
    {
        World world = GameManager.Instance.World;

        if (world == null)
        {
            return false;
        }

        if (!world.CanMobsSpawnAtPos(position))
        {
            Logging.Debug("CanMobsSpawnAtPos returned false for position {0}", position);
            return false;
        }

        if (world.isPositionInRangeOfBedrolls(position))
        {
            Logging.Debug("Position {0} is near a bedroll", position);
            return false;
        }

        return true;
    }

    public static int GetEntityClassId(Chunk chunk, UnityEngine.Vector3 worldPos)
    {
        World world = GameManager.Instance.World;

        if (world == null || chunk is null)
        {
            return -1;
        }

        byte biomeId = chunk.GetBiomeId(
            World.toBlockXZ(Mathf.FloorToInt(worldPos.x)),
            World.toBlockXZ(Mathf.FloorToInt(worldPos.z))
        );

        BiomeDefinition biomeData = world.Biomes.GetBiome(biomeId);

        if (biomeData is null
                || !BiomeSpawningClass.list.TryGetValue(biomeData.m_sBiomeName,
                    out BiomeSpawnEntityGroupList biomeList))
        {
            return -1;
        }

        if (!chunk.IsAreaMasterDominantBiomeInitialized(world.ChunkClusters[0]))
        {
            return -1;
        }

        ChunkAreaBiomeSpawnData spawnData = chunk.GetChunkBiomeSpawnData();

        if (!spawnData.checkedPOITags)
        {
            spawnData.checkedPOITags = true;

            FastTags<TagGroup.Poi> tags = FastTags<TagGroup.Poi>.none;
            Vector3i chunkWorldPos = spawnData.chunk.GetWorldPos();
            world.GetPOIsAtXZ(
                    chunkWorldPos.x + 16,
                    chunkWorldPos.x + 80 - 16,
                    chunkWorldPos.z + 16,
                    chunkWorldPos.z + 80 - 16,
                    spawnPIs);

            foreach (PrefabInstance pi in spawnPIs)
            {
                tags |= pi.prefab.Tags;
            }

            spawnData.poiTags = tags;
            bool isEmpty = tags.IsEmpty;

            for (int i = 0; i < biomeList.list.Count; i++)
            {
                BiomeSpawnEntityGroupData group = biomeList.list[i];

                bool unrestricted = group.POITags.IsEmpty
                    && group.noPOITags.IsEmpty;
                bool restricted = !group.noPOITags.IsEmpty
                    && group.noPOITags.Test_AnySet(tags);
                bool matchedEmpty = group.POITags.Test_AnySet(EmptyTag)
                    && !group.POITags.IsEmpty
                    && tags.IsEmpty;
                bool matchedTag = !group.POITags.IsEmpty
                    && !tags.IsEmpty
                    && group.POITags.Test_AnySet(tags);

                if(!restricted && (unrestricted || matchedEmpty || matchedTag))
                {
                    spawnData.groupsEnabledFlags |= 1 << i;
                }
            }
        }

        EDaytime eDaytime = world.IsDaytime() ? EDaytime.Day : EDaytime.Night;
        List<BiomeSpawnEntityGroupData> validGroups = [];

        for (int i = 0; i < biomeList.list.Count; i++)
        {
            BiomeSpawnEntityGroupData group = biomeList.list[i];

            if ((spawnData.groupsEnabledFlags & (1 << i)) != 0
                    && (group.daytime == eDaytime || group.daytime == EDaytime.Any))
            {
                validGroups.Add(group);
            }
        }

        GameRandom random = world.GetGameRandom();
        validGroups = [.. validGroups.OrderBy(_ => random.Next())];
        BiomeSpawnEntityGroupData selectedGroup = null;
        string name = "";
        bool spawnEnemies = GameStats.GetBool(EnumGameStats.IsSpawnEnemies);

        foreach (BiomeSpawnEntityGroupData group in validGroups)
        {
            bool isEnemyGroup = EntityGroups.IsEnemyGroup(group.entityGroupRefName);

            if (isEnemyGroup && !spawnEnemies)
            {
                continue;
            }

            int maxCount = group.maxCount;

            if (isEnemyGroup)
            {
                maxCount = EntitySpawner.ModifySpawnCountByGameDifficulty(maxCount);
            }

            name = BGSS.Common.GetSpawnGroupRefName(biomeId, group);
            int hash = BGSS.Common.GetSpawnGroupHash(biomeId, group);

            // Calculate the current game stage using Rect
            int playersGS = BGSS.Common.CalcChunkAreaGameStage(spawnData.chunk.GetAABB());

            // Get min/max game stage values for this entity group from the XML
            if (BGSS.Common.SpawnData.TryGetValue(hash, out var validGroup))
            {
                if(!string.IsNullOrWhiteSpace(validGroup.RequiredDecoTag))
                {
                    continue;
                }

                int minGS = validGroup.Min;
                int maxGS = validGroup.Max;

                // Check if the current game stage falls within the min/max range
                if (playersGS < minGS || playersGS > maxGS)
                {
                    continue;
                }
            }

            if (spawnData.GetEntitiesSpawned(name) < maxCount)
            {
                selectedGroup = group;
                break;
            }
        }

        if (selectedGroup == null)
        {
            return -1;
        }

        int lastClassId = -1;
        int randFromGroup = EntityGroups.GetRandomFromGroup(
                selectedGroup.entityGroupRefName, ref lastClassId);

        if (randFromGroup is (-1) or 0)
        {
            spawnData.SetRespawnDelay(name, world.worldTime, world.Biomes);
            return -1;
        }

        spawnData.IncEntitiesSpawned(name);
        return randFromGroup;
    }

    public static int SpawnAgent(Simulation simulation, Agent agent)
    {
        BGSS.Common.LoadBiomeGameStages();

        if (GameManager.Instance.World is not {} world
                || !CanSpawnZombie())
        {
            return -1;
        }

        var worldPos = VectorUtils.ToUnity(agent.Position);

        // We leave y position to be adjusted by the terrain.
        worldPos.y = 0;

        var chunkPosX = World.toChunkXZ(Mathf.FloorToInt(worldPos.x));
        var chunkPosZ = World.toChunkXZ(Mathf.FloorToInt(worldPos.z));

        if (world.GetChunkSync(chunkPosX, chunkPosZ) is not Chunk chunk)
        {
            Logging.DebugErr("Failed to spawn agent, chunk not loaded at {0}, {1}", chunkPosX, chunkPosZ);
            return -1;
        }

        var terrainHeight = world.GetTerrainHeight(Mathf.FloorToInt(worldPos.x), Mathf.FloorToInt(worldPos.z)) + 1;
        Logging.Debug("Terrain height at {0}, {1} is {2}", worldPos.x, worldPos.z, terrainHeight);

        // Adjust position height.
        worldPos.y = terrainHeight;

        if (!CanSpawnAtPosition(worldPos))
        {
            Logging.Debug("Failed to spawn agent, position not suitable at {0}", worldPos);
            return -1;
        }

        if (simulation.Config.Debug.LogSpawnDespawn)
        {
            Logging.Out("Spawning agent at {0}", worldPos);
        }

        // Use previously assigned entity class id.
        int entityClassId = agent.EntityClassId;
        if (entityClassId is (-1) or 0)
        {
            entityClassId = GetEntityClassId(chunk, worldPos);

            if (entityClassId is (-1) or 0)
            {
                // If we failed to get one, use the fallback group for the
                // biome, or the general fallback group if none specified.
                BiomeDefinition biome = Utils.GetBiomeAt((int)worldPos.x, (int)worldPos.y);
                int discard = 0;

                if (simulation.Config.Biomes.FirstOrDefault(a => a.Name == biome.m_sBiomeName) is { } biomeCfg
                        && !string.IsNullOrWhiteSpace(biomeCfg.FallbackSpawnGroup))
                {
                    entityClassId = EntityGroups.GetRandomFromGroup(biomeCfg.FallbackSpawnGroup, ref discard);
                }
                else
                {
                    entityClassId = EntityGroups.GetRandomFromGroup(simulation.Config.FallbackSpawnGroup, ref discard);
                }

                if(entityClassId is (-1) or 0)
                {
                    Logging.Info("Failed to get a valid entity class id.");
                    return -1;
                }
            }
        }
        else
        {
            Logging.Debug("Using previous entity class id: {0}", entityClassId);
        }

        var rot = VectorUtils.ToUnity(agent.Velocity);
        rot.y = 0;
        rot.Normalize();

        var spawnedAgent = EntityFactory.CreateEntity(entityClassId, worldPos, rot) as EntityAlive;
        if (spawnedAgent == null)
        {
            Logging.DebugErr("Unable to create zombie entity!, Class Id: {0}, Pos: {1}", entityClassId, worldPos);
            return -1;
        }

        // Only update last class id if we successfully spawned the agent.
        spawnedAgent.bIsChunkObserver = true;
        spawnedAgent.SetSpawnerSource(EnumSpawnerSource.StaticSpawner);
        spawnedAgent.moveDirection = rot;

        if (spawnedAgent is EntityZombie spawnedZombie)
        {
            spawnedZombie.IsHordeZombie = true;
        }

        if (agent.Health != -1)
        {
            Logging.Debug("Using previous health: {0}", agent.Health);
            spawnedAgent.Health = agent.Health;
        }

        var destPos = worldPos + (rot * 150);
        spawnedAgent.SetInvestigatePosition(destPos, 6000, false);

        world.SpawnEntityInWorld(spawnedAgent);

        // Update the agent data.
        agent.EntityId = spawnedAgent.entityId;
        agent.EntityClassId = entityClassId;
        agent.CurrentState = Agent.State.Active;
        agent.Health = spawnedAgent.Health;

        if (simulation.Config.Debug.LogSpawnDespawn)
        {
            Logging.Out("Agent spawned at {0}, entity id {1}, class id {2}", worldPos, spawnedAgent.entityId, entityClassId);
        }

        return spawnedAgent.entityId;
    }

    internal static bool DespawnAgent(Simulation simulation, Agent agent)
    {
        var world = GameManager.Instance.World;
        if (world == null)
        {
            return false;
        }

        var entity = world.GetEntity(agent.EntityId) as EntityZombie;
        if (entity == null)
        {
            Logging.Debug("Entity not found: {0}", agent.EntityId);
            agent.ResetSpawnData();
            return false;
        }

        // Retain current state.
        agent.Health = entity.Health;

        agent.Velocity = VectorUtils.ToSim(entity.moveDirection);
        agent.Velocity.Validate();

        agent.Position = VectorUtils.ToSim(entity.position);
        agent.Position.Validate();

        world.RemoveEntity(entity.entityId, EnumRemoveEntityReason.Despawned);

        if (simulation.Config.Debug.LogSpawnDespawn)
        {
            Logging.Out("Agent despawned, entity id: {0}", agent.EntityId);
        }

        return true;
    }
}
