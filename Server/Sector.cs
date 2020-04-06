/***************************************************************************
 *                                 Sector.cs
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
using System.Collections.Generic;
using Server.Items;
using Server.Network;

namespace Server
{
    public class RegionRect : IComparable
    {
        private Region m_Region;
        private Rectangle3D m_Rect;

        public Region Region { get { return m_Region; } }
        public Rectangle3D Rect { get { return m_Rect; } }

        public LinkedListNode<RegionRect> Node;

        public RegionRect(Region region, Rectangle3D rect)
        {
            m_Region = region;
            m_Rect = rect;
            Node = new LinkedListNode<RegionRect>(this);
        }

        public bool Contains(Point3D loc)
        {
            return m_Rect.Contains(loc);
        }

        int IComparable.CompareTo(object obj)
        {
            if (obj == null)
                return 1;

            RegionRect regRect = obj as RegionRect;

            if (regRect == null)
                throw new ArgumentException("obj is not a RegionRect", "obj");

            return ((IComparable)m_Region).CompareTo(regRect.m_Region);
        }
    }


    public class Sector
    {
        private int m_X, m_Y;
        private Map m_Owner;
        private bool m_Active;

        private LinkedList<Mobile> m_Mobiles = new LinkedList<Mobile>();
        private LinkedList<Item> m_Items = new LinkedList<Item>();
        private LinkedList<NetState> m_Clients = new LinkedList<NetState>();
        private LinkedList<BaseMulti> m_Multis = new LinkedList<BaseMulti>();
        private LinkedList<RegionRect> m_RegionRects = new LinkedList<RegionRect>();

        public Sector(int x, int y, Map owner)
        {
            m_X = x;
            m_Y = y;
            m_Owner = owner;
            m_Active = false;
        }

        public void OnClientChange(NetState oldState, NetState newState)
        {
            if (oldState != null)
            {
                if (oldState.Node.List == m_Clients)
                {
                    m_Clients.Remove(oldState.Node);
                }
            }

            if (newState != null)
            {
                if (newState.Node.List == null)
                {
                    m_Clients.AddLast(newState.Node);
                }
            }
        }

        public void OnEnter(Item item)
        {
            if (item.Node.List != null)
                item.Node.List.Remove(item.Node);

            m_Items.AddLast(item.Node);
        }

        public void OnLeave(Item item)
        {
            if (item.Node.List == m_Items)
                m_Items.Remove(item.Node);
        }

        public void OnEnter(Mobile mob)
        {
            if (mob.Node.List != null)
                mob.Node.List.Remove(mob.Node);

            m_Mobiles.AddLast(mob.Node);

            if (mob.NetState != null)
            {
                if (m_Clients.Count == 0)
                {
                    m_Owner.ActivateSectors(m_X, m_Y);
                }
                if (mob.NetState.Node.List != null)
                    mob.NetState.Node.List.Remove(mob.NetState.Node);

                m_Clients.AddLast(mob.NetState.Node);
            }
        }

        public void OnLeave(Mobile mob)
        {
            if (mob.Node.List == m_Mobiles)
                m_Mobiles.Remove(mob.Node);

            if (mob.NetState != null)
            {
                if (mob.NetState.Node.List == m_Clients)
                    m_Clients.Remove(mob.NetState.Node);

                if (m_Clients.Count == 0)
                {
                    m_Owner.DeactivateSectors(m_X, m_Y);
                }
            }
        }

        public void OnEnter(Region region, Rectangle3D rect)
        {
            RegionRect rr = new RegionRect(region, rect);

            if (rr.Node.List != null)
                rr.Node.List.Remove(rr.Node);

            var node = m_RegionRects.First;

            while (node != null)
            {
                IComparable comp = node.Value as IComparable;
                if (comp.CompareTo(rr) > 0)
                {
                    break;
                }

                node = node.Next;
            }

            if (node == null)
            {
                m_RegionRects.AddLast(rr.Node);
            }
            else
            {
                m_RegionRects.AddBefore(node, rr.Node);
            }

            UpdateMobileRegions();
        }

        public void OnLeave(Region region)
        {
            LinkedListNode<RegionRect> node = m_RegionRects.Last;

            while (node != null)
            {
                RegionRect rr = node.Value;

                node = node.Previous;

                if (rr.Region == region)
                {
                    if (rr.Node.List == m_RegionRects)
                        m_RegionRects.Remove(rr.Node);
                }
            }

            UpdateMobileRegions();
        }

        private void UpdateMobileRegions()
        {
            LinkedListNode<Mobile> node = m_Mobiles.Last;

            while (node != null)
            {
                node.Value.UpdateRegion();
                node = node.Previous;
            }
        }

        public void OnMultiEnter(BaseMulti multi)
        {
            m_Multis.AddLast(multi);
        }

        public void OnMultiLeave(BaseMulti multi)
        {
            m_Multis.Remove(multi);
        }

        public void Activate()
        {
            if (!Active && m_Owner != Map.Internal)
            {
                if (m_Items != null)
                {
                    var inode = m_Items.Last;
                    while (inode != null)
                    {
                        var item = inode.Value;
                        inode = inode.Previous;
                        item.OnSectorActivate();
                    }
                }

                var node = m_Mobiles.Last;

                while (node != null)
                {
                    var mob = node.Value;

                    node = node.Previous;

                    mob.OnSectorActivate();
                }

                m_Active = true;
            }
        }

        public void Deactivate()
        {
            if (Active)
            {
                if (Items != null)
                {
                    var inode = m_Items.Last;
                    while (inode != null)
                    {
                        var item = inode.Value;
                        inode = inode.Previous;
                        item.OnSectorDeactivate();
                    }
                }

                var mnode = m_Mobiles.Last;

                while (mnode != null)
                {
                    var mob = mnode.Value;

                    mnode = mnode.Previous;

                    mob.OnSectorDeactivate();
                }

                m_Active = false;
            }
        }

        public IEnumerable<RegionRect> RegionRects
        {
            get
            {
                return m_RegionRects;
            }
        }

        public IEnumerable<BaseMulti> Multis
        {
            get
            {
                return m_Multis;
            }
        }

        public IEnumerable<Mobile> Mobiles
        {
            get
            {
                return m_Mobiles;
            }
        }

        public IEnumerable<Item> Items
        {
            get
            {
                return m_Items;
            }
        }

        public IEnumerable<NetState> Clients
        {
            get
            {
                return m_Clients;
            }
        }

        #region Reverse Iterators

        /* These are only to be used by Map.cs */

        public LinkedListNode<RegionRect> LastRegionRect()
        {
            return m_RegionRects.Last;
        }

        public LinkedListNode<BaseMulti> LastMulti()
        {
            return m_Multis.Last;
        }

        public LinkedListNode<Server.Mobile> LastMobile()
        {
            return m_Mobiles.Last;
        }

        public LinkedListNode<Server.Item> LastItem()
        {
            return m_Items.Last;
        }

        public LinkedListNode<NetState> LastClient()
        {
            return m_Clients.Last;
        }

        #endregion

        public bool Active
        {
            get
            {
                return (m_Active && m_Owner != Map.Internal);
            }
        }

        public Map Owner
        {
            get
            {
                return m_Owner;
            }
        }

        public int X
        {
            get
            {
                return m_X;
            }
        }

        public int Y
        {
            get
            {
                return m_Y;
            }
        }
    }
}
