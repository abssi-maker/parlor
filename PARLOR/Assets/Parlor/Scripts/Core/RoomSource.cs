using System.Collections.Generic;
using UnityEngine;

namespace Parlor
{
    /// <summary>A horizontal surface the stage can stand on.</summary>
    public struct TableSurface
    {
        /// <summary>Centre of the top surface. Up is the surface normal; forward and right are the rectangle's axes.</summary>
        public Pose Pose;
        /// <summary>Extent along the pose's right (x) and forward (y) axes, in metres.</summary>
        public Vector2 Size;
        public string Uuid;
    }

    /// <summary>
    /// A scanned piece of furniture or clutter as an oriented box (MRUK volume). Used to keep the stage off real objects
    /// standing on a table. Point-in-box tests go through WorldToLocal into Local.
    /// </summary>
    public struct RoomBox
    {
        public Matrix4x4 WorldToLocal;
        public Bounds Local;
        public float TopY, BottomY;     // world heights of the box's highest and lowest corners

        public bool ContainsXZ(Vector3 world, float pad)
        {
            // test on the box's own axes, ignoring height (the caller has already checked the height overlap)
            Vector3 l = WorldToLocal.MultiplyPoint3x4(world);
            Vector3 e = Local.extents + Vector3.one * pad, c = Local.center;
            // the box's vertical axis is whichever local axis is most aligned with world up; skip that one
            Vector3 up = WorldToLocal.MultiplyVector(Vector3.up);
            int ua = Mathf.Abs(up.x) > Mathf.Abs(up.y) ? (Mathf.Abs(up.x) > Mathf.Abs(up.z) ? 0 : 2) : (Mathf.Abs(up.y) > Mathf.Abs(up.z) ? 1 : 2);
            for (int i = 0; i < 3; i++) if (i != ua && Mathf.Abs(l[i] - c[i]) > e[i]) return false;
            return true;
        }
    }

    /// <summary>A vertical surface for the curtain call.</summary>
    public struct WallSurface
    {
        /// <summary>Centre of the wall. Forward points out of the wall, into the room.</summary>
        public Pose Pose;
        public Vector2 Size;   // width, height
    }

    /// <summary>
    /// What the show needs to know about the real room. The Quest implementation reads MR Utility Kit; tests use a
    /// scripted one; with no implementation at all (or one that fails) the show falls back to its floating table.
    /// </summary>
    public interface IRoomSource
    {
        /// <summary>True once the room has finished loading, successfully or not.</summary>
        bool Settled { get; }
        /// <summary>True when scene data is actually available.</summary>
        bool Available { get; }
        void Tables(List<TableSurface> into);
        void Walls(List<WallSurface> into);
        /// <summary>Scanned objects (not tables, walls, floor or ceiling) the stage must not stand on or through.</summary>
        void Obstacles(List<RoomBox> into) { }
        /// <summary>World height of the real floor, when the room knows it.</summary>
        bool TryFloorY(out float y) { y = 0f; return false; }
    }
}
