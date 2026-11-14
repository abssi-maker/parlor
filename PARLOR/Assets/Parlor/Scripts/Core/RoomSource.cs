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
    }
}
