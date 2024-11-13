using System;
using System.Collections.Generic;
using UnityEngine;

namespace WalkerSim;

internal static class Utils
{
    private static BiomeTextureData _cachedBiomeTextureData;
    private static Dictionary<string,float> _cachedBiomeProportions;

    public static TimeSpan Measure(Action action)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        action();
        watch.Stop();
        return watch.Elapsed;
    }

    public static string GetWorldLocationString(Config.WorldLocation value)
    {
        switch (value)
        {
            case Config.WorldLocation.None:
                return "None";
            case Config.WorldLocation.RandomBorderLocation:
                return "Random Border Location";
            case Config.WorldLocation.RandomLocation:
                return "Random Location";
            case Config.WorldLocation.RandomPOI:
                return "Random POI";
            case Config.WorldLocation.Mixed:
                return "Mixed";
            default:
                break;
        }
        throw new Exception("Invalid value");
    }

    public static Vector3 GetRandomVector3(System.Random prng, Vector3 mins, Vector3 maxs, float borderSize = 250)
    {
        float x0 = (float)prng.NextDouble();
        float y0 = (float)prng.NextDouble();
        float x = Math.Remap(x0, 0f, 1f, mins.X + borderSize, maxs.X - borderSize);
        float y = Math.Remap(y0, 0f, 1f, mins.Y + borderSize, maxs.Y - borderSize);
        return new Vector3(x, y);
    }

    public static System.Drawing.Color ParseColor(string color)
    {
        if (color == "")
        {
            return System.Drawing.Color.Transparent;
        }
        try
        {
            var res = (System.Drawing.Color)System.Drawing.ColorTranslator.FromHtml(color);
            return res;
        }
        catch (Exception)
        {
            return System.Drawing.Color.Transparent;
        }
    }

    public static string ColorToHexString(System.Drawing.Color color)
    {
        return System.Drawing.ColorTranslator.ToHtml(color);
    }

    public static BiomeDefinition GetBiomeAt(int x, int z)
    {
        if (GameManager.Instance.World is not { } world)
        {
            return null;
        }

        if (_cachedBiomeTextureData is null)
        {
            PathAbstractions.AbstractedLocation worldDirPath =
                PathAbstractions.WorldsSearchPaths.GetLocation(
                        GamePrefs.GetString(EnumGamePrefs.GameWorld));
            Texture2D biomeTex = TextureUtils.LoadTexture(
                    worldDirPath.FullPath + "/biomes.png");

            BiomeTextureData data = new()
            {
                rawData = biomeTex.GetRawTextureData(),
                width = biomeTex.width,
                height = biomeTex.height
            };

            _cachedBiomeTextureData = data;
        }

        BiomeTextureData texData = _cachedBiomeTextureData;
        Vector2i worldSizeVec = world.ChunkCache.ChunkProvider.GetWorldSize();
        int blocksPerPixel = worldSizeVec.x / texData.width;
        x += worldSizeVec.x / 2;
        z += worldSizeVec.y / 2;
        x -= x % 16;
        z -= z % 16;
        x /= blocksPerPixel;
        z /= blocksPerPixel;
        z = texData.height - z;
        uint idx = ((uint)(z * texData.width) + (uint)(x % texData.width)) * 4;

        uint colour = ((uint)texData.rawData[idx + 1] << 16) |
            ((uint)texData.rawData[idx + 2] << 8) |
            (texData.rawData[idx + 3]);

        return world.Biomes.GetBiomeMap().TryGetValue(
                colour, out BiomeDefinition biome) ? biome : null;
    }

    public static Dictionary<string, float> GetBiomeProportions()
    {
        if (_cachedBiomeProportions != null)
        {
            return _cachedBiomeProportions;
        }

        if (GameManager.Instance.World is not {} world)
        {
            return null;
        }

        // Load in the biomes.png for the world so we can figure out the
        // size of each biome.
        PathAbstractions.AbstractedLocation worldDirPath =
            PathAbstractions.WorldsSearchPaths.GetLocation(
                    GamePrefs.GetString(EnumGamePrefs.GameWorld));
        Texture2D biomeTex = TextureUtils.LoadTexture(
                worldDirPath.FullPath + "/biomes.png");

        // Get size of world in blocks.
        Vector2i worldSizeVec = world.ChunkCache.ChunkProvider.GetWorldSize();
        int dimRatio = worldSizeVec.x / biomeTex.width;
        int worldSize = worldSizeVec.x * worldSizeVec.y / (dimRatio * dimRatio);

        // Prepare to count blocks in each biome based on colour.
        Dictionary<uint, BiomeDefinition> biomeDefs = world.Biomes.GetBiomeMap();
        Dictionary<string, float> proportions = [];
        byte[] biomeTexPixData = biomeTex.GetRawTextureData();

        foreach (BiomeDefinition biomeDef in biomeDefs.Values)
        {
            int blocks = 0;

            // Scan the array for blocks with matching colour and sum them.
            for (int i = 0; i < biomeTexPixData.Length; i += 4)
            {
                uint c =
                    ((uint)biomeTexPixData[i + 1] << 16) +
                    ((uint)biomeTexPixData[i + 2] << 8) +
                    biomeTexPixData[i + 3];

                if (c == biomeDef.m_uiColor)
                {
                    blocks++;
                }
            }

            // Figure out biome's proportion of world map based on the block
            // count for the biome / the total block count for the world.
            proportions[biomeDef.m_sBiomeName] = (float)blocks / worldSize;
        }

        _cachedBiomeProportions ??= proportions;
        return proportions;
    }

    public static void ClearCachedData()
    {
        _cachedBiomeProportions = null;
        _cachedBiomeTextureData = null;
    }
}

public class BiomeTextureData
{
    public byte[] rawData;
    public int width;
    public int height;
}
