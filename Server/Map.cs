/***************************************************************************
 *                                  Map.cs
 *                            -------------------
 *   begin                : May 1, 2002
 *   copyright            : (C) The RunUO Software Team
 *   email                : info@runuo.com
 *
 *   $Id$
 *
 ***************************************************************************/

/***************************************************************************
 *
 *   This program is free software; you can redistribute it and/or modify
 *   it under the terms of the GNU General Public License as published by
 *   the Free Software Foundation; either version 2 of the License, or
 *   (at your option) any later version.
 *
 ***************************************************************************/

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Server.Items;
using Server.Network;
using Server.Targeting;

namespace Server
{
    [Flags]
    public enum MapRules
    {
        None = 0x0000,
        Internal = 0x0001, // Internal map (used for dragging, commodity deeds, etc)
        FreeMovement = 0x0002, // Anyone can move over anyone else without taking stamina loss
        BeneficialRestrictions = 0x0004, // Disallow performing beneficial actions on criminals/murderers
        HarmfulRestrictions = 0x0008, // Disallow performing harmful actions on innocents
        TrammelRules = FreeMovement | BeneficialRestrictions | HarmfulRestrictions,
        FeluccaRules = None
    }

    [Parsable]
    public sealed class Map : IComparable, IComparable<Map>
    {
        public const int SectorSize = 16;
        public const int SectorShift = 4;
        public static int SectorActiveRange = 2;

        private static Map[] m_Maps = new Map[0x100];

        public static Map[] Maps { get { return m_Maps; } }

        public static Map Felucca { get { return m_Maps[0]; } }
        public static Map Trammel { get { return m_Maps[1]; } }
        public static Map Ilshenar { get { return m_Maps[2]; } }
        public static Map Malas { get { return m_Maps[3]; } }
        public static Map Tokuno { get { return m_Maps[4]; } }
        public static Map TerMur { get { return m_Maps[5]; } }
        public static Map Internal { get { return m_Maps[0x7F]; } }

        private static List<Map> m_AllMaps = new List<Map>();

        public static List<Map> AllMaps { get { return m_AllMaps; } }

        private int m_MapID, m_MapIndex, m_FileIndex;

        private int m_Width, m_Height;
        private int m_SectorsWidth, m_SectorsHeight;
        private int m_Season;
        private Dictionary<string, Region> m_Regions;
        private Region m_DefaultRegion;

        public int Season { get { return m_Season; } set { m_Season = value; } }

        private string m_Name;
        private MapRules m_Rules;
        private Sector[][] m_Sectors;
        private Sector m_InvalidSector;

        private TileMatrix m_Tiles;

        private static string[] m_MapNames;
        private static Map[] m_MapValues;

        public static string[] GetMapNames()
        {
            CheckNamesAndValues();
            return m_MapNames;
        }

        public static Map[] GetMapValues()
        {
            CheckNamesAndValues();
            return m_MapValues;
        }

        public static Map Parse(string value)
        {
            CheckNamesAndValues();

            for (int i = 0; i < m_MapNames.Length; ++i)
            {
                if (Insensitive.Equals(m_MapNames[i], value))
                    return m_MapValues[i];
            }

            int index;

            if (int.TryParse(value, out index))
            {
                if (index >= 0 && index < m_Maps.Length && m_Maps[index] != null)
                    return m_Maps[index];
            }

            throw new ArgumentException("Invalid map name");
        }

        private static void CheckNamesAndValues()
        {
            if (m_MapNames != null && m_MapNames.Length == m_AllMaps.Count)
                return;

            m_MapNames = new string[m_AllMaps.Count];
            m_MapValues = new Map[m_AllMaps.Count];

            for (int i = 0; i < m_AllMaps.Count; ++i)
            {
                Map map = m_AllMaps[i];

                m_MapNames[i] = map.Name;
                m_MapValues[i] = map;
            }
        }

        public override string ToString()
        {
            return m_Name;
        }

        public int GetAverageZ(int x, int y)
        {
            int z = 0, avg = 0, top = 0;

            GetAverageZ(x, y, ref z, ref avg, ref top);

            return avg;
        }

        public void GetAverageZ(int x, int y, ref int z, ref int avg, ref int top)
        {
            int zTop = Tiles.GetLandTile(x, y).Z;
            int zLeft = Tiles.GetLandTile(x, y + 1).Z;
            int zRight = Tiles.GetLandTile(x + 1, y).Z;
            int zBottom = Tiles.GetLandTile(x + 1, y + 1).Z;

            z = zTop;
            if (zLeft < z)
                z = zLeft;
            if (zRight < z)
                z = zRight;
            if (zBottom < z)
                z = zBottom;

            top = zTop;
            if (zLeft > top)
                top = zLeft;
            if (zRight > top)
                top = zRight;
            if (zBottom > top)
                top = zBottom;

            if (Math.Abs(zTop - zBottom) > Math.Abs(zLeft - zRight))
                avg = FloorAverage(zLeft, zRight);
            else
                avg = FloorAverage(zTop, zBottom);
        }

        private static int FloorAverage(int a, int b)
        {
            int v = a + b;

            if (v < 0)
                --v;

            return (v / 2);
        }

        #region Get*InRange/Bounds
        public IEnumerable<IEntity> GetObjectsInRange(Point3D p)
        {
            return GetObjectsInRange(p, 18);
        }

        public IEnumerable<IEntity> GetObjectsInRange(Point3D p, int range)
        {
            return GetObjectsInBounds(new Rectangle2D(p.m_X - range, p.m_Y - range, range * 2 + 1, range * 2 + 1));
        }

        public IEnumerable<IEntity> GetObjectsInBounds(Rectangle2D bounds)
        {
            foreach (var mobile in GetMobilesInBounds(bounds))
            {
                yield return mobile;
            }

            foreach (var item in GetItemsInBounds(bounds))
            {
                yield return item;
            }
        }

        public IEnumerable<NetState> GetClientsInRange(Point3D p)
        {
            return GetClientsInRange(p, 18);
        }

        public IEnumerable<NetState> GetClientsInRange(Point3D p, int range)
        {
            return GetClientsInBounds(new Rectangle2D(p.m_X - range, p.m_Y - range, range * 2 + 1, range * 2 + 1));
        }

        public IEnumerable<NetState> GetClientsInBounds(Rectangle2D bounds)
        {
            int sectorStartX, sectorStartY, sectorEndX, sectorEndY;

            CalculateSectors(bounds, out sectorStartX, out sectorStartY, out sectorEndX, out sectorEndY);

            int numSectors = ((sectorEndX - sectorStartX) + 1) * ((sectorEndY - sectorStartY) + 1);

            // Allocate a list to hold the end of the list for each sector.
            var ends = new List<LinkedListNode<NetState>>(numSectors);

            for (int i = sectorStartX; i <= sectorEndX; i++)
            {
                for (int j = sectorStartY; j <= sectorEndY; j++)
                {
                    var sector = GetRealSector(i, j);

                    ends.Add(sector.LastClient());
                }
            }

            foreach (var list in ends)
            {
                /* Iterate the mobiles list in reverse. New elements are
                 * always added to the end of the list, so if we start
                 * at the end and go back to the beginning, then new
                 * mobiles created during the loop won't be picked
                 * up.
                 */
                var node = list;

                while (node != null)
                {
                    var o = node.Value;

                    /* Move the node to the previous one here, prior to
                     * yielding the mobile. That way, if the mobile removes
                     * itself from the list the iteration does not break. */
                    node = node.Previous;

                    if (o != null && o.Mobile != null && !o.Mobile.Deleted && bounds.Contains(o.Mobile))
                    {
                        yield return o;
                    }
                }
            }
        }

        public IEnumerable<Item> GetItemsInRange(Point3D p)
        {
            return GetItemsInRange(p, 18);
        }

        public IEnumerable<Item> GetItemsInRange(Point3D p, int range)
        {
            return GetItemsInBounds(new Rectangle2D(p.m_X - range, p.m_Y - range, range * 2 + 1, range * 2 + 1));
        }

        public IEnumerable<Item> GetItemsInBounds(Rectangle2D bounds)
        {
            int sectorStartX, sectorStartY, sectorEndX, sectorEndY;

            CalculateSectors(bounds, out sectorStartX, out sectorStartY, out sectorEndX, out sectorEndY);

            int numSectors = ((sectorEndX - sectorStartX) + 1) * ((sectorEndY - sectorStartY) + 1);

            // Allocate a list to hold the end of the list for each sector.
            var ends = new List<LinkedListNode<Item>>(numSectors);

            for (int i = sectorStartX; i <= sectorEndX; i++)
            {
                for (int j = sectorStartY; j <= sectorEndY; j++)
                {
                    var sector = GetRealSector(i, j);

                    ends.Add(sector.LastItem());
                }
            }

            foreach (var list in ends)
            {
                /* Iterate the mobiles list in reverse. New elements are
                 * always added to the end of the list, so if we start
                 * at the end and go back to the beginning, then new
                 * mobiles created during the loop won't be picked
                 * up.
                 */
                var node = list;

                while (node != null)
                {
                    var o = node.Value;

                    /* Move the node to the previous one here, prior to
                     * yielding the mobile. That way, if the mobile removes
                     * itself from the list the iteration does not break. */
                    node = node.Previous;

                    if (o != null && !o.Deleted && o.Parent == null && bounds.Contains(o))
                    {
                        yield return o;
                    }
                }
            }
        }

        public IEnumerable<Mobile> GetMobilesInRange(Point3D p)
        {
            return GetMobilesInRange(p, 18);
        }

        public IEnumerable<Mobile> GetMobilesInRange(Point3D p, int range)
        {
            return GetMobilesInBounds(new Rectangle2D(p.m_X - range, p.m_Y - range, range * 2 + 1, range * 2 + 1));
        }

        public IEnumerable<Mobile> GetMobilesInBounds(Rectangle2D bounds)
        {
            int sectorStartX, sectorStartY, sectorEndX, sectorEndY;

            CalculateSectors(bounds, out sectorStartX, out sectorStartY, out sectorEndX, out sectorEndY);

            int numSectors = ((sectorEndX - sectorStartX) + 1) * ((sectorEndY - sectorStartY) + 1);

            // Allocate a list to hold the end of the list for each sector.
            var ends = new List<LinkedListNode<Mobile>>(numSectors);

            for (int i = sectorStartX; i <= sectorEndX; i++)
            {
                for (int j = sectorStartY; j <= sectorEndY; j++)
                {
                    var sector = GetRealSector(i, j);

                    ends.Add(sector.LastMobile());
                }
            }

            foreach (var list in ends)
            {
                /* Iterate the mobiles list in reverse. New elements are
                 * always added to the end of the list, so if we start
                 * at the end and go back to the beginning, then new
                 * mobiles created during the loop won't be picked
                 * up.
                 */
                var node = list;

                while (node != null)
                {
                    var o = node.Value;

                    /* Move the node to the previous one here, prior to
                     * yielding the mobile. That way, if the mobile removes
                     * itself from the list the iteration does not break. */
                    node = node.Previous;

                    if (o != null && !o.Deleted && bounds.Contains(o))
                    {
                        yield return o;
                    }
                }
            }
        }
        #endregion

        public IEnumerable<StaticTile[]> GetMultiTilesAt(int tx, int ty)
        {
            Rectangle2D bounds = new Rectangle2D(tx, ty, 1, 1);

            int sectorStartX, sectorStartY, sectorEndX, sectorEndY;

            CalculateSectors(bounds, out sectorStartX, out sectorStartY, out sectorEndX, out sectorEndY);

            int numSectors = ((sectorEndX - sectorStartX) + 1) * ((sectorEndY - sectorStartY) + 1);

            // Allocate a list to hold the end of the list for each sector.
            var ends = new List<LinkedListNode<BaseMulti>>(numSectors);

            for (int i = sectorStartX; i <= sectorEndX; i++)
            {
                for (int j = sectorStartY; j <= sectorEndY; j++)
                {
                    var sector = GetRealSector(i, j);

                    ends.Add(sector.LastMulti());
                }
            }

            foreach (var list in ends)
            {
                /* Iterate the mobiles list in reverse. New elements are
                 * always added to the end of the list, so if we start
                 * at the end and go back to the beginning, then new
                 * mobiles created during the loop won't be picked
                 * up.
                 */
                var node = list;

                while (node != null)
                {
                    var o = node.Value;

                    /* Move the node to the previous one here, prior to
                     * yielding the mobile. That way, if the mobile removes
                     * itself from the list the iteration does not break. */
                    node = node.Previous;

                    if (o != null && !o.Deleted)
                    {
                        var c = o.Components;

                        int x, y, xo, yo;
                        StaticTile[] t, r;

                        for (x = bounds.Start.X; x < bounds.End.X; x++)
                        {
                            xo = x - (o.X + c.Min.X);

                            if (xo < 0 || xo >= c.Width)
                            {
                                continue;
                            }

                            for (y = bounds.Start.Y; y < bounds.End.Y; y++)
                            {
                                yo = y - (o.Y + c.Min.Y);

                                if (yo < 0 || yo >= c.Height)
                                {
                                    continue;
                                }

                                t = c.Tiles[xo][yo];

                                if (t.Length <= 0)
                                {
                                    continue;
                                }

                                r = new StaticTile[t.Length];

                                for (var i = 0; i < t.Length; i++)
                                {
                                    r[i] = t[i];
                                    r[i].Z += o.Z;
                                }

                                yield return r;
                            }
                        }
                    }
                }
            }
        }

        #region CanFit
        public bool CanFit(Point3D p, int height, bool checkBlocksFit)
        {
            return CanFit(p.m_X, p.m_Y, p.m_Z, height, checkBlocksFit, true, true);
        }

        public bool CanFit(Point3D p, int height, bool checkBlocksFit, bool checkMobiles)
        {
            return CanFit(p.m_X, p.m_Y, p.m_Z, height, checkBlocksFit, checkMobiles, true);
        }

        public bool CanFit(Point2D p, int z, int height, bool checkBlocksFit)
        {
            return CanFit(p.m_X, p.m_Y, z, height, checkBlocksFit, true, true);
        }

        public bool CanFit(Point3D p, int height)
        {
            return CanFit(p.m_X, p.m_Y, p.m_Z, height, false, true, true);
        }

        public bool CanFit(Point2D p, int z, int height)
        {
            return CanFit(p.m_X, p.m_Y, z, height, false, true, true);
        }

        public bool CanFit(int x, int y, int z, int height)
        {
            return CanFit(x, y, z, height, false, true, true);
        }

        public bool CanFit(int x, int y, int z, int height, bool checksBlocksFit)
        {
            return CanFit(x, y, z, height, checksBlocksFit, true, true);
        }

        public bool CanFit(int x, int y, int z, int height, bool checkBlocksFit, bool checkMobiles)
        {
            return CanFit(x, y, z, height, checkBlocksFit, checkMobiles, true);
        }

        public bool CanFit(int x, int y, int z, int height, bool checkBlocksFit, bool checkMobiles, bool requireSurface)
        {
            if (this == Map.Internal)
                return false;

            if (x < 0 || y < 0 || x >= m_Width || y >= m_Height)
                return false;

            bool hasSurface = false;

            LandTile lt = Tiles.GetLandTile(x, y);
            int lowZ = 0, avgZ = 0, topZ = 0;

            GetAverageZ(x, y, ref lowZ, ref avgZ, ref topZ);
            TileFlag landFlags = TileData.LandTable[lt.ID & TileData.MaxLandValue].Flags;

            if ((landFlags & TileFlag.Impassable) != 0 && avgZ > z && (z + height) > lowZ)
                return false;
            else if ((landFlags & TileFlag.Impassable) == 0 && z == avgZ && !lt.Ignored)
                hasSurface = true;

            StaticTile[] staticTiles = Tiles.GetStaticTiles(x, y, true);

            bool surface, impassable;

            for (int i = 0; i < staticTiles.Length; ++i)
            {
                ItemData id = TileData.ItemTable[staticTiles[i].ID & TileData.MaxItemValue];
                surface = id.Surface;
                impassable = id.Impassable;

                if ((surface || impassable) && (staticTiles[i].Z + id.CalcHeight) > z && (z + height) > staticTiles[i].Z)
                    return false;
                else if (surface && !impassable && z == (staticTiles[i].Z + id.CalcHeight))
                    hasSurface = true;
            }

            Sector sector = GetSector(x, y);
            var items = sector.Items;
            var mobs = sector.Mobiles;

            foreach (var item in items)
            {
                if (!(item is BaseMulti) && item.ItemID <= TileData.MaxItemValue && item.AtWorldPoint(x, y))
                {
                    ItemData id = item.ItemData;
                    surface = id.Surface;
                    impassable = id.Impassable;

                    if ((surface || impassable || (checkBlocksFit && item.BlocksFit)) && (item.Z + id.CalcHeight) > z && (z + height) > item.Z)
                        return false;
                    else if (surface && !impassable && !item.Movable && z == (item.Z + id.CalcHeight))
                        hasSurface = true;
                }
            }

            if (checkMobiles)
            {
                foreach (var m in mobs)
                {
                    if (m.Location.m_X == x && m.Location.m_Y == y && (m.AccessLevel == AccessLevel.Player || !m.Hidden))
                    {
                        if ((m.Z + 16) > z && (z + height) > m.Z)
                            return false;
                    }
                }
            }

            return !requireSurface || hasSurface;
        }

        #endregion

        #region CanSpawnMobile
        public bool CanSpawnMobile(Point3D p)
        {
            return CanSpawnMobile(p.m_X, p.m_Y, p.m_Z);
        }

        public bool CanSpawnMobile(Point2D p, int z)
        {
            return CanSpawnMobile(p.m_X, p.m_Y, z);
        }

        public bool CanSpawnMobile(int x, int y, int z)
        {
            if (!Region.Find(new Point3D(x, y, z), this).AllowSpawn())
                return false;

            return CanFit(x, y, z, 16);
        }
        #endregion

        private class ZComparer : IComparer<Item>
        {
            public static readonly ZComparer Default = new ZComparer();

            public int Compare(Item x, Item y)
            {
                return x.Z.CompareTo(y.Z);
            }
        }

        public void FixColumn(int x, int y)
        {
            LandTile landTile = Tiles.GetLandTile(x, y);

            int landZ = 0, landAvg = 0, landTop = 0;
            GetAverageZ(x, y, ref landZ, ref landAvg, ref landTop);

            StaticTile[] tiles = Tiles.GetStaticTiles(x, y, true);

            List<Item> items = new List<Item>();

            foreach (Item item in GetItemsInRange(new Point3D(x, y, 0), 0))
            {
                if (!(item is BaseMulti) && item.ItemID <= TileData.MaxItemValue)
                {
                    items.Add(item);

                    if (items.Count > 100)
                        break;
                }
            }

            if (items.Count > 100)
                return;

            items.Sort(ZComparer.Default);

            for (int i = 0; i < items.Count; ++i)
            {
                Item toFix = items[i];

                if (!toFix.Movable)
                    continue;

                int z = int.MinValue;
                int currentZ = toFix.Z;

                if (!landTile.Ignored && landAvg <= currentZ)
                    z = landAvg;

                for (int j = 0; j < tiles.Length; ++j)
                {
                    StaticTile tile = tiles[j];
                    ItemData id = TileData.ItemTable[tile.ID & TileData.MaxItemValue];

                    int checkZ = tile.Z;
                    int checkTop = checkZ + id.CalcHeight;

                    if (checkTop == checkZ && !id.Surface)
                        ++checkTop;

                    if (checkTop > z && checkTop <= currentZ)
                        z = checkTop;
                }

                for (int j = 0; j < items.Count; ++j)
                {
                    if (j == i)
                        continue;

                    Item item = items[j];
                    ItemData id = item.ItemData;

                    int checkZ = item.Z;
                    int checkTop = checkZ + id.CalcHeight;

                    if (checkTop == checkZ && !id.Surface)
                        ++checkTop;

                    if (checkTop > z && checkTop <= currentZ)
                        z = checkTop;
                }

                if (z != int.MinValue)
                    toFix.Location = new Point3D(toFix.X, toFix.Y, z);
            }
        }

        /// <summary>
        /// Gets the highest surface that is lower than <paramref name="p"/>.
        /// </summary>
        /// <param name="p">The reference point.</param>
        /// <returns>A surface <typeparamref name="Tile"/> or <typeparamref name="Item"/>.</returns>
        public object GetTopSurface(Point3D p)
        {
            if (this == Map.Internal)
                return null;

            object surface = null;
            int surfaceZ = int.MinValue;


            LandTile lt = Tiles.GetLandTile(p.X, p.Y);

            if (!lt.Ignored)
            {
                int avgZ = GetAverageZ(p.X, p.Y);

                if (avgZ <= p.Z)
                {
                    surface = lt;
                    surfaceZ = avgZ;

                    if (surfaceZ == p.Z)
                        return surface;
                }
            }

            StaticTile[] staticTiles = Tiles.GetStaticTiles(p.X, p.Y, true);

            for (int i = 0; i < staticTiles.Length; i++)
            {
                StaticTile tile = staticTiles[i];
                ItemData id = TileData.ItemTable[tile.ID & TileData.MaxItemValue];

                if (id.Surface || (id.Flags & TileFlag.Wet) != 0)
                {
                    int tileZ = tile.Z + id.CalcHeight;

                    if (tileZ > surfaceZ && tileZ <= p.Z)
                    {
                        surface = tile;
                        surfaceZ = tileZ;

                        if (surfaceZ == p.Z)
                            return surface;
                    }
                }
            }

            Sector sector = GetSector(p.X, p.Y);

            foreach (var item in sector.Items)
            {
                if (!(item is BaseMulti) && item.ItemID <= TileData.MaxItemValue && item.AtWorldPoint(p.X, p.Y) && !item.Movable)
                {
                    ItemData id = item.ItemData;

                    if (id.Surface || (id.Flags & TileFlag.Wet) != 0)
                    {
                        int itemZ = item.Z + id.CalcHeight;

                        if (itemZ > surfaceZ && itemZ <= p.Z)
                        {
                            surface = item;
                            surfaceZ = itemZ;

                            if (surfaceZ == p.Z)
                                return surface;
                        }
                    }
                }
            }

            return surface;
        }

        public void Bound(int x, int y, out int newX, out int newY)
        {
            if (x < 0)
                newX = 0;
            else if (x >= m_Width)
                newX = m_Width - 1;
            else
                newX = x;

            if (y < 0)
                newY = 0;
            else if (y >= m_Height)
                newY = m_Height - 1;
            else
                newY = y;
        }

        public Point2D Bound(Point2D p)
        {
            int x = p.m_X, y = p.m_Y;

            if (x < 0)
                x = 0;
            else if (x >= m_Width)
                x = m_Width - 1;

            if (y < 0)
                y = 0;
            else if (y >= m_Height)
                y = m_Height - 1;

            return new Point2D(x, y);
        }

        private void CalculateSectors(Rectangle2D bounds,
                                      out int sectorStartX, out int sectorStartY,
                                      out int sectorEndX, out int sectorEndY)
        {
            int left = bounds.Start.X;
            int top = bounds.Start.Y;
            int right = bounds.End.X;
            int bottom = bounds.End.Y;

            // Limit the coordinates to inside the valid map region
            Bound(left, top, out left, out top);
            Bound(right - 1, bottom - 1, out right, out bottom);

            // Calculate the top left sector
            sectorStartX = left >> Map.SectorShift; // left / 16
            sectorStartY = top >> Map.SectorShift; // top / 16

            // Calculate the bottom right sector.
            sectorEndX = right >> Map.SectorShift; // right / 16
            sectorEndY = bottom >> Map.SectorShift; // bottom / 16
        }

        public Map(int mapID, int mapIndex, int fileIndex, int width, int height, int season, string name, MapRules rules)
        {
            m_MapID = mapID;
            m_MapIndex = mapIndex;
            m_FileIndex = fileIndex;
            m_Width = width;
            m_Height = height;
            m_Season = season;
            m_Name = name;
            m_Rules = rules;
            m_Regions = new Dictionary<string, Region>(StringComparer.OrdinalIgnoreCase);
            m_InvalidSector = new Sector(0, 0, this);
            m_SectorsWidth = width >> SectorShift;
            m_SectorsHeight = height >> SectorShift;
            m_Sectors = new Sector[m_SectorsWidth][];
        }

        #region GetSector
        public Sector GetSector(Point3D p)
        {
            return InternalGetSector(p.m_X >> SectorShift, p.m_Y >> SectorShift);
        }

        public Sector GetSector(Point2D p)
        {
            return InternalGetSector(p.m_X >> SectorShift, p.m_Y >> SectorShift);
        }

        public Sector GetSector(IPoint2D p)
        {
            return InternalGetSector(p.X >> SectorShift, p.Y >> SectorShift);
        }

        public Sector GetSector(int x, int y)
        {
            return InternalGetSector(x >> SectorShift, y >> SectorShift);
        }

        public Sector GetRealSector(int x, int y)
        {
            return InternalGetSector(x, y);
        }

        private Sector InternalGetSector(int x, int y)
        {
            if (x >= 0 && x < m_SectorsWidth && y >= 0 && y < m_SectorsHeight)
            {
                Sector[] xSectors = m_Sectors[x];

                if (xSectors == null)
                    m_Sectors[x] = xSectors = new Sector[m_SectorsHeight];

                Sector sec = xSectors[y];

                if (sec == null)
                    xSectors[y] = sec = new Sector(x, y, this);

                return sec;
            }
            else
            {
                return m_InvalidSector;
            }
        }
        #endregion

        public void ActivateSectors(int cx, int cy)
        {
            for (int x = cx - SectorActiveRange; x <= cx + SectorActiveRange; ++x)
            {
                for (int y = cy - SectorActiveRange; y <= cy + SectorActiveRange; ++y)
                {
                    Sector sect = GetRealSector(x, y);
                    if (sect != m_InvalidSector)
                        sect.Activate();
                }
            }
        }

        public void DeactivateSectors(int cx, int cy)
        {
            for (int x = cx - SectorActiveRange; x <= cx + SectorActiveRange; ++x)
            {
                for (int y = cy - SectorActiveRange; y <= cy + SectorActiveRange; ++y)
                {
                    Sector sect = GetRealSector(x, y);
                    if (sect != m_InvalidSector && !PlayersInRange(sect, SectorActiveRange))
                        sect.Deactivate();
                }
            }
        }

        private bool PlayersInRange(Sector sect, int range)
        {
            for (int x = sect.X - range; x <= sect.X + range; ++x)
            {
                for (int y = sect.Y - range; y <= sect.Y + range; ++y)
                {
                    Sector check = GetRealSector(x, y);
                    if (check != m_InvalidSector && check.Clients.Any())
                        return true;
                }
            }

            return false;
        }

        public void OnClientChange(NetState oldState, NetState newState, Mobile m)
        {
            if (this == Map.Internal)
                return;

            GetSector(m).OnClientChange(oldState, newState);
        }

        public void OnEnter(Mobile m)
        {
            if (this == Map.Internal)
                return;

            Sector sector = GetSector(m);

            sector.OnEnter(m);
        }

        public void OnEnter(Item item)
        {
            if (this == Map.Internal)
                return;

            GetSector(item).OnEnter(item);

            if (item is BaseMulti)
            {
                BaseMulti m = (BaseMulti)item;
                MultiComponentList mcl = m.Components;

                Sector start = GetMultiMinSector(item.Location, mcl);
                Sector end = GetMultiMaxSector(item.Location, mcl);

                AddMulti(m, start, end);
            }
        }

        public void OnLeave(Mobile m)
        {
            if (this == Map.Internal)
                return;

            Sector sector = GetSector(m);

            sector.OnLeave(m);
        }

        public void OnLeave(Item item)
        {
            if (this == Map.Internal)
                return;

            GetSector(item).OnLeave(item);

            if (item is BaseMulti)
            {
                BaseMulti m = (BaseMulti)item;
                MultiComponentList mcl = m.Components;

                Sector start = GetMultiMinSector(item.Location, mcl);
                Sector end = GetMultiMaxSector(item.Location, mcl);

                RemoveMulti(m, start, end);
            }
        }

        public void RemoveMulti(BaseMulti m, Sector start, Sector end)
        {
            if (this == Map.Internal)
                return;

            for (int x = start.X; x <= end.X; ++x)
                for (int y = start.Y; y <= end.Y; ++y)
                    InternalGetSector(x, y).OnMultiLeave(m);
        }

        public void AddMulti(BaseMulti m, Sector start, Sector end)
        {
            if (this == Map.Internal)
                return;

            for (int x = start.X; x <= end.X; ++x)
                for (int y = start.Y; y <= end.Y; ++y)
                    InternalGetSector(x, y).OnMultiEnter(m);
        }

        public Sector GetMultiMinSector(Point3D loc, MultiComponentList mcl)
        {
            return GetSector(Bound(new Point2D(loc.m_X + mcl.Min.m_X, loc.m_Y + mcl.Min.m_Y)));
        }

        public Sector GetMultiMaxSector(Point3D loc, MultiComponentList mcl)
        {
            return GetSector(Bound(new Point2D(loc.m_X + mcl.Max.m_X, loc.m_Y + mcl.Max.m_Y)));
        }

        public void OnMove(Point3D oldLocation, Mobile m)
        {
            if (this == Map.Internal)
                return;

            Sector oldSector = GetSector(oldLocation);
            Sector newSector = GetSector(m.Location);

            if (oldSector != newSector)
            {
                oldSector.OnLeave(m);
                newSector.OnEnter(m);
            }
        }

        public void OnMove(Point3D oldLocation, Item item)
        {
            if (this == Map.Internal)
                return;

            Sector oldSector = GetSector(oldLocation);
            Sector newSector = GetSector(item.Location);

            if (oldSector != newSector)
            {
                oldSector.OnLeave(item);
                newSector.OnEnter(item);
            }

            if (item is BaseMulti)
            {
                BaseMulti m = (BaseMulti)item;
                MultiComponentList mcl = m.Components;

                Sector start = GetMultiMinSector(item.Location, mcl);
                Sector end = GetMultiMaxSector(item.Location, mcl);

                Sector oldStart = GetMultiMinSector(oldLocation, mcl);
                Sector oldEnd = GetMultiMaxSector(oldLocation, mcl);

                if (oldStart != start || oldEnd != end)
                {
                    RemoveMulti(m, oldStart, oldEnd);
                    AddMulti(m, start, end);
                }
            }
        }

        private object tileLock = new object();

        public TileMatrix Tiles
        {
            get
            {
                if (m_Tiles != null)
                    return m_Tiles;

                lock (tileLock)
                    return m_Tiles ?? (m_Tiles = new TileMatrix(this, m_FileIndex, m_MapID, m_Width, m_Height));
            }
        }

        public int MapID
        {
            get
            {
                return m_MapID;
            }
        }

        public int MapIndex
        {
            get
            {
                return m_MapIndex;
            }
        }

        public int Width
        {
            get
            {
                return m_Width;
            }
        }

        public int Height
        {
            get
            {
                return m_Height;
            }
        }

        public Dictionary<string, Region> Regions
        {
            get
            {
                return m_Regions;
            }
        }

        public void RegisterRegion(Region reg)
        {
            string regName = reg.Name;

            if (regName != null)
            {
                if (m_Regions.ContainsKey(regName))
                    Console.WriteLine("Warning: Duplicate region name '{0}' for map '{1}'", regName, this.Name);
                else
                    m_Regions[regName] = reg;
            }
        }

        public void UnregisterRegion(Region reg)
        {
            string regName = reg.Name;

            if (regName != null)
                m_Regions.Remove(regName);
        }

        public Region DefaultRegion
        {
            get
            {
                if (m_DefaultRegion == null)
                    m_DefaultRegion = new Region(null, this, 0, new Rectangle3D[0]);

                return m_DefaultRegion;
            }
            set
            {
                m_DefaultRegion = value;
            }
        }

        public MapRules Rules
        {
            get
            {
                return m_Rules;
            }
            set
            {
                m_Rules = value;
            }
        }

        public Sector InvalidSector
        {
            get
            {
                return m_InvalidSector;
            }
        }

        public string Name
        {
            get
            {
                return m_Name;
            }
            set
            {
                m_Name = value;
            }
        }

        public class NullEnumerable<T> : IEnumerable<T>
        {
            public static readonly NullEnumerable<T> Instance = new NullEnumerable<T>();

            private readonly IEnumerable<T> _Empty;

            private NullEnumerable()
            {
                _Empty = Enumerable.Empty<T>();
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return _Empty.GetEnumerator();
            }

            public IEnumerator<T> GetEnumerator()
            {
                return _Empty.GetEnumerator();
            }

            public void Free()
            { }
        }

        public Point3D GetPoint(object o, bool eye)
        {
            Point3D p;

            if (o is Mobile)
            {
                p = ((Mobile)o).Location;
                p.Z += 14;//eye ? 15 : 10;
            }
            else if (o is Item)
            {
                p = ((Item)o).GetWorldLocation();
                p.Z += (((Item)o).ItemData.Height / 2) + 1;
            }
            else if (o is Point3D)
            {
                p = (Point3D)o;
            }
            else if (o is LandTarget)
            {
                p = ((LandTarget)o).Location;

                int low = 0, avg = 0, top = 0;
                GetAverageZ(p.X, p.Y, ref low, ref avg, ref top);

                p.Z = top + 1;
            }
            else if (o is StaticTarget)
            {
                StaticTarget st = (StaticTarget)o;
                ItemData id = TileData.ItemTable[st.ItemID & TileData.MaxItemValue];

                p = new Point3D(st.X, st.Y, st.Z - id.CalcHeight + (id.Height / 2) + 1);
            }
            else if (o is IPoint3D)
            {
                p = new Point3D((IPoint3D)o);
            }
            else
            {
                Console.WriteLine("Warning: Invalid object ({0}) in line of sight", o);
                p = Point3D.Zero;
            }

            return p;
        }

        #region Line Of Sight
        private static int m_MaxLOSDistance = 25;

        public static int MaxLOSDistance
        {
            get { return m_MaxLOSDistance; }
            set { m_MaxLOSDistance = value; }
        }

        public bool LineOfSight(Point3D org, Point3D dest)
        {
            if (this == Map.Internal)
                return false;

            if (!Utility.InRange(org, dest, m_MaxLOSDistance))
                return false;

            Point3D start = org;
            Point3D end = dest;

            if (org.X > dest.X || (org.X == dest.X && org.Y > dest.Y) || (org.X == dest.X && org.Y == dest.Y && org.Z > dest.Z))
            {
                Point3D swap = org;
                org = dest;
                dest = swap;
            }

            double rise, run, zslp;
            double sq3d;
            double x, y, z;
            int xd, yd, zd;
            int ix, iy, iz;
            int height;
            bool found;
            Point3D p;
            Point3DList path = new Point3DList();
            TileFlag flags;

            if (org == dest)
                return true;

            if (path.Count > 0)
                path.Clear();

            xd = dest.m_X - org.m_X;
            yd = dest.m_Y - org.m_Y;
            zd = dest.m_Z - org.m_Z;
            zslp = Math.Sqrt(xd * xd + yd * yd);
            if (zd != 0)
                sq3d = Math.Sqrt(zslp * zslp + zd * zd);
            else
                sq3d = zslp;

            rise = ((float)yd) / sq3d;
            run = ((float)xd) / sq3d;
            zslp = ((float)zd) / sq3d;

            y = org.m_Y;
            z = org.m_Z;
            x = org.m_X;
            while (Utility.NumberBetween(x, dest.m_X, org.m_X, 0.5) && Utility.NumberBetween(y, dest.m_Y, org.m_Y, 0.5) && Utility.NumberBetween(z, dest.m_Z, org.m_Z, 0.5))
            {
                ix = (int)Math.Round(x);
                iy = (int)Math.Round(y);
                iz = (int)Math.Round(z);
                if (path.Count > 0)
                {
                    p = path.Last;

                    if (p.m_X != ix || p.m_Y != iy || p.m_Z != iz)
                        path.Add(ix, iy, iz);
                }
                else
                {
                    path.Add(ix, iy, iz);
                }
                x += run;
                y += rise;
                z += zslp;
            }

            if (path.Count == 0)
                return true;//<--should never happen, but to be safe.

            p = path.Last;

            if (p != dest)
                path.Add(dest);

            Point3D pTop = org, pBottom = dest;
            Utility.FixPoints(ref pTop, ref pBottom);

            int pathCount = path.Count;
            int endTop = end.m_Z + 1;

            for (int i = 0; i < pathCount; ++i)
            {
                Point3D point = path[i];
                int pointTop = point.m_Z + 1;

                LandTile landTile = Tiles.GetLandTile(point.X, point.Y);
                int landZ = 0, landAvg = 0, landTop = 0;
                GetAverageZ(point.m_X, point.m_Y, ref landZ, ref landAvg, ref landTop);

                if (landZ <= pointTop && landTop >= point.m_Z && (point.m_X != end.m_X || point.m_Y != end.m_Y || landZ > endTop || landTop < end.m_Z) && !landTile.Ignored)
                    return false;

                /* --Do land tiles need to be checked?  There is never land between two people, always statics.--
				LandTile landTile = Tiles.GetLandTile( point.X, point.Y );
				if ( landTile.Z-1 >= point.Z && landTile.Z+1 <= point.Z && (TileData.LandTable[landTile.ID & TileData.MaxLandValue].Flags & TileFlag.Impassable) != 0 )
					return false;
				*/

                StaticTile[] statics = Tiles.GetStaticTiles(point.m_X, point.m_Y, true);

                bool contains = false;
                int ltID = landTile.ID;

                for (int j = 0; !contains && j < m_InvalidLandTiles.Length; ++j)
                    contains = (ltID == m_InvalidLandTiles[j]);

                if (contains && statics.Length == 0)
                {
                    foreach (Item item in GetItemsInRange(point, 0))
                    {
                        if (item.Visible)
                            contains = false;

                        if (!contains)
                            break;
                    }

                    if (contains)
                        return false;
                }

                for (int j = 0; j < statics.Length; ++j)
                {
                    StaticTile t = statics[j];

                    ItemData id = TileData.ItemTable[t.ID & TileData.MaxItemValue];

                    flags = id.Flags;
                    height = id.CalcHeight;

                    if (t.Z <= pointTop && t.Z + height >= point.Z && (flags & (TileFlag.Window | TileFlag.NoShoot)) != 0)
                    {
                        if (point.m_X == end.m_X && point.m_Y == end.m_Y && t.Z <= endTop && t.Z + height >= end.m_Z)
                            continue;

                        return false;
                    }

                    /*if ( t.Z <= point.Z && t.Z+height >= point.Z && (flags&TileFlag.Window)==0 && (flags&TileFlag.NoShoot)!=0
						&& ( (flags&TileFlag.Wall)!=0 || (flags&TileFlag.Roof)!=0 || (((flags&TileFlag.Surface)!=0 && zd != 0)) ) )*/
                    /*{
						//Console.WriteLine( "LoS: Blocked by Static \"{0}\" Z:{1} T:{3} P:{2} F:x{4:X}", TileData.ItemTable[t.ID&TileData.MaxItemValue].Name, t.Z, point, t.Z+height, flags );
						//Console.WriteLine( "if ( {0} && {1} && {2} && ( {3} || {4} || {5} || ({6} && {7} && {8}) ) )", t.Z <= point.Z, t.Z+height >= point.Z, (flags&TileFlag.Window)==0, (flags&TileFlag.Impassable)!=0, (flags&TileFlag.Wall)!=0, (flags&TileFlag.Roof)!=0, (flags&TileFlag.Surface)!=0, t.Z != dest.Z, zd != 0 ) ;
						return false;
					}*/
                }
            }

            Rectangle2D rect = new Rectangle2D(pTop.m_X, pTop.m_Y, (pBottom.m_X - pTop.m_X) + 1, (pBottom.m_Y - pTop.m_Y) + 1);

            foreach (Item i in GetItemsInBounds(rect))
            {
                if (!i.Visible)
                    continue;

                if (i is BaseMulti || i.ItemID > TileData.MaxItemValue)
                    continue;

                ItemData id = i.ItemData;
                flags = id.Flags;

                if ((flags & (TileFlag.Window | TileFlag.NoShoot)) == 0)
                    continue;

                height = id.CalcHeight;

                found = false;

                int count = path.Count;

                for (int j = 0; j < count; ++j)
                {
                    Point3D point = path[j];
                    int pointTop = point.m_Z + 1;
                    Point3D loc = i.Location;

                    //if ( t.Z <= point.Z && t.Z+height >= point.Z && ( height != 0 || ( t.Z == dest.Z && zd != 0 ) ) )
                    if (loc.m_X == point.m_X && loc.m_Y == point.m_Y &&
                        loc.m_Z <= pointTop && loc.m_Z + height >= point.m_Z)
                    {
                        if (loc.m_X == end.m_X && loc.m_Y == end.m_Y && loc.m_Z <= endTop && loc.m_Z + height >= end.m_Z)
                            continue;

                        found = true;
                        break;
                    }
                }

                if (!found)
                    continue;

                return false;

                /*if ( (flags & (TileFlag.Impassable | TileFlag.Surface | TileFlag.Roof)) != 0 )

				//flags = TileData.ItemTable[i.ItemID&TileData.MaxItemValue].Flags;
				//if ( (flags&TileFlag.Window)==0 && (flags&TileFlag.NoShoot)!=0 && ( (flags&TileFlag.Wall)!=0 || (flags&TileFlag.Roof)!=0 || (((flags&TileFlag.Surface)!=0 && zd != 0)) ) )
				{
					//height = TileData.ItemTable[i.ItemID&TileData.MaxItemValue].Height;
					//Console.WriteLine( "LoS: Blocked by ITEM \"{0}\" P:{1} T:{2} F:x{3:X}", TileData.ItemTable[i.ItemID&TileData.MaxItemValue].Name, i.Location, i.Location.Z+height, flags );
					area.Free();
					return false;
				}*/
            }

            return true;
        }

        public bool LineOfSight(object from, object dest)
        {
            if (from == dest || (from is Mobile && ((Mobile)from).AccessLevel > AccessLevel.Player))
                return true;
            else if (dest is Item && from is Mobile && ((Item)dest).RootParent == from)
                return true;

            return LineOfSight(GetPoint(from, true), GetPoint(dest, false));
        }

        public bool LineOfSight(Mobile from, Point3D target)
        {
            if (from.AccessLevel > AccessLevel.Player)
                return true;

            Point3D eye = from.Location;

            eye.Z += 14;

            return LineOfSight(eye, target);
        }

        public bool LineOfSight(Mobile from, Mobile to)
        {
            if (from == to || from.AccessLevel > AccessLevel.Player)
                return true;

            Point3D eye = from.Location;
            Point3D target = to.Location;

            eye.Z += 14;
            target.Z += 14;//10;

            return LineOfSight(eye, target);
        }
        #endregion

        private static int[] m_InvalidLandTiles = new int[] { 0x244 };

        public static int[] InvalidLandTiles
        {
            get { return m_InvalidLandTiles; }
            set { m_InvalidLandTiles = value; }
        }

        public int CompareTo(Map other)
        {
            if (other == null)
                return -1;

            return m_MapID.CompareTo(other.m_MapID);
        }

        public int CompareTo(object other)
        {
            if (other == null || other is Map)
                return this.CompareTo(other);

            throw new ArgumentException();
        }
    }
}
