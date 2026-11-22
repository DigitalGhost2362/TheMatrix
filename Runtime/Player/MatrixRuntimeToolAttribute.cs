#nullable enable
using System;

namespace Feeder.MCP.Player
{
    /// <summary>
    /// Marks a static method as a tool that runs inside a built player (or in Play mode) instead of the Unity Editor.
    /// The method runs on Unity's main thread. Parameters may be string, bool, int, long, float, double or an enum;
    /// decorate them with <see cref="System.ComponentModel.DescriptionAttribute"/>. It returns a string or any object
    /// that serializes to JSON. Add <c>[UnityEngine.Scripting.Preserve]</c> so IL2CPP stripping keeps it.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class MatrixRuntimeToolAttribute : Attribute
    {
        public string Name { get; }
        public string? Title { get; set; }
        public string? Description { get; set; }

        /// <summary>Tools that only read the running game. They skip the editor lock on the matrix.</summary>
        public bool ReadOnly { get; set; }

        public MatrixRuntimeToolAttribute(string name) => Name = name;
    }
}
