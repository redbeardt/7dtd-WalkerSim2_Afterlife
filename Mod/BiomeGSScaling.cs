using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System.Xml.Linq;

namespace WalkerSim.BiomeGSScaling;

public struct SpawnData
{
    public int Min;
    public int Max;
    public string RequiredDecoTag;
}

public class Common
{
    public static Dictionary<int, SpawnData> SpawnData { get; set; } = new();
    public static Dictionary<int, int> BiomeSpawnGamestages { get; set; } = new();
    public static bool GamestagesParsed { get; set; }

    public static int GetSpawnGroupHash(
            int biomeId,
            BiomeSpawnEntityGroupData group) =>
        GetSpawnGroupRefName(biomeId, group).GetHashCode();

    public static string GetSpawnGroupRefName(
            int biomeId,
            BiomeSpawnEntityGroupData group) =>
        $"{biomeId}_{group.entityGroupRefName}_{group.daytime}_{group.maxCount}";

    public static int CalcChunkAreaGameStage(Bounds chunkBounds)
    {
        var playerGameStages = new List<int>();
        var spawnedPlayers = GameManager.Instance.World.GetPlayers().Where(
                static a => a.Spawned);

        foreach(EntityPlayer p in spawnedPlayers){
            UnityEngine.Vector3 playerBoundsCenter = new(p.position.x, 128f, p.position.z);
            UnityEngine.Vector3 playerBoundsSize = new(80, 256f, 80f);
            Bounds playerBounds = new(playerBoundsCenter, playerBoundsSize);

            if(playerBounds.Intersects(chunkBounds)){
                playerGameStages.Add(p.gameStage);
            }
        }

        return GameStageDefinition.CalcPartyLevel(playerGameStages);
    }

	public static void LoadBiomeGameStages()
	{
		if (GamestagesParsed) {
			return;
		}

		var commonData = SpawnData;

		foreach(var data in commonData){
			SpawnData gsData = data.Value;
			int hash = data.Key;

			if(!BiomeSpawnGamestages.ContainsKey(hash)){
				int result = gsData.Min;
                BiomeSpawnGamestages.Add(hash, result);
			}

            GamestagesParsed = true;
		}
	}
}

[HarmonyPatch(typeof(BiomeSpawningFromXml))]
public static class PatchesBiomeSpawningFromXml
{
	private static XmlFile xmlFile;

	[HarmonyPatch(nameof(BiomeSpawningFromXml.Load))]
	[HarmonyPrefix]
	private static void LoadPrefix(XmlFile _xmlFile)
	{
		xmlFile = _xmlFile;
	}

	[HarmonyPatch(typeof(BiomeSpawningFromXml), "Load", MethodType.Enumerator)]
	[HarmonyPostfix]
	private static void LoadPostfix()
	{
		if(xmlFile is not {} f) {
			return;
		}

		XElement root = f.XmlDoc.Root;

		foreach(var biomeEl in root.Elements("biome")){
			int index = 0;
			string biomeName = biomeEl.GetAttribute("name");

			// If a group list was not ingested for this biome, skip it.
			if (!BiomeSpawningClass.list.TryGetValue(biomeName, out var groupList)) {
				Log.Warning($"BiomeSpawningClass.list had no biome with \"{biomeName}\".", 2);
				continue;
			}

			foreach(var spawnEl in biomeEl.Elements("spawn")) {
				// Make sure the xml has a group name and the data was previously
				// ingested by original function before trying to do anything else.
				if (!spawnEl.HasAttribute("entitygroup")) {
					Log.Warning("Element had no entitygroup. Skipping...", 2);
					continue;
				}

				BiomeSpawnEntityGroupData groupData = groupList.list[index++];
				SpawnData gsData = new(){
					Min = 0,
					Max = int.MaxValue
				};

				if (spawnEl.HasAttribute("mings")){
					gsData.Min = int.Parse(spawnEl.GetAttribute("mings"));
				}

				if (spawnEl.HasAttribute("maxgs")){
					gsData.Max = int.Parse(spawnEl.GetAttribute("maxgs"));
				}

				if(spawnEl.HasAttribute("required_deco_tag")){
					gsData.RequiredDecoTag = spawnEl.GetAttribute("required_deco_tag");
				}
				
				// Make sure to set, not add, in case of reingestion.
				int biomeId = BiomeDefinition.nameToId[biomeName];
				int hash = Common.GetSpawnGroupHash(biomeId, groupData);

				if(Common.SpawnData.TryGetValue(hash, out var storedGSData)){
					Common.SpawnData[hash] = gsData;
					continue;
				}

				Common.SpawnData.Add(hash, gsData);
			}
		}
	}
}
