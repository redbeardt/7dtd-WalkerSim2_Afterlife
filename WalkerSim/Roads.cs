﻿using System;
using System.Collections.Generic;
using System.Drawing;

namespace WalkerSim
{
    enum RoadType : byte
    {
        None = 0,
        Asphalt,
        Offroad,
    }

    internal class Roads
    {
        const int ScaledWidth = 768;
        const int ScaledHeight = 768;

        const int GridX = 32;
        const int GridY = 32;

        RoadType[] _data;
        int _width;
        int _height;

        public struct RoadPoint
        {
            public static RoadPoint Invalid = new RoadPoint()
            {
                Type = RoadType.None,
                X = -1,
                Y = -1,
            };

            public RoadType Type;
            public int X;
            public int Y;
        }

        struct Cell
        {
            public List<RoadPoint> Points;
        }

        Cell[] _roadGrid;
        int _gridWidth;
        int _gridHeight;

        public int Width
        {
            get => _width;
        }

        public int Height
        {
            get => _height;
        }

        // This functions expects data to be RGBA with 8 bits per channel.
        // The data is expected to be in row-major order.
        public bool LoadFromData(UInt32[] data, int width, int height)
        {
            return true;
        }

        public static Roads LoadFromBitmap(Bitmap img)
        {
            // Resize the image to 712x712
            var scaled = new Bitmap(img, ScaledWidth, ScaledHeight);

            var height = scaled.Height;
            var width = scaled.Width;

            var data = new RoadType[width * height];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var pixel = scaled.GetPixel(x, y);
                    if (pixel.R != 0)
                    {
                        data[y * width + x] = RoadType.Asphalt;
                    }
                    else if (pixel.G != 0)
                    {
                        data[y * width + x] = RoadType.Offroad;
                    }
                }
            }

            var res = new Roads();
            res._width = width;
            res._height = height;
            res._data = data;

            // Create the grid.
            res._gridWidth = (int)Math.Ceiling((float)width / GridX);
            res._gridHeight = (int)Math.Ceiling((float)height / GridY);
            res._roadGrid = new Cell[res._gridWidth * res._gridHeight];
            for (int i = 0; i < res._roadGrid.Length; i++)
            {
                res._roadGrid[i] = new Cell()
                {
                    Points = new List<RoadPoint>(),
                };
            }

            // Extract all the data and store it in the cells.
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var roadType = data[y * width + x];
                    if (roadType == RoadType.None)
                        continue;

                    var cellX = x / GridX;
                    var cellY = y / GridY;
                    var cellIndex = cellY * res._gridWidth + cellX;
                    var cell = res._roadGrid[cellIndex];

                    var point = new RoadPoint()
                    {
                        Type = roadType,
                        X = x,
                        Y = y,
                    };
                    cell.Points.Add(point);
                }
            }

            // Minimize the amount of points, we can delete every second point.
            for (int i = 0; i < res._roadGrid.Length; i++)
            {
                var cell = res._roadGrid[i];
                if (cell.Points.Count == 0)
                {
                    continue;
                }

                var newPoints = new List<RoadPoint>();
                for (int j = 0; j < cell.Points.Count; j++)
                {
                    if (j % 2 == 0)
                    {
                        newPoints.Add(cell.Points[j]);
                    }
                }

                cell.Points = newPoints;
            }

            return res;
        }

        public RoadType GetRoadType(int x, int y)
        {
            int index = y * _width + x;
            if (index < 0 || index >= _data.Length)
                return RoadType.None;

            return _data[index];
        }

        public RoadPoint GetClosestRoad(int x, int y)
        {
            var closest = RoadPoint.Invalid;

            var cellX = x / GridX;
            var cellY = y / GridY;
            var cellIndex = cellY * _gridWidth + cellX;
            var cell = _roadGrid[cellIndex];

            float closestDist = float.MaxValue;
            if (cell.Points.Count == 0)
            {
                return closest;
            }

            for (int i = 0; i < cell.Points.Count; i++)
            {
                var point = cell.Points[i];
                if (point.X == x && point.Y == y)
                {
                    continue;
                }

                var dist = Vector3.DistanceSqr(new Vector3(x, y), new Vector3(point.X, point.Y));
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closest = point;
                }
            }

            return closest;
        }

    }
}