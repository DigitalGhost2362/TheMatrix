#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Feeder.MCP.Editor.Tests.Reflect
{
    public enum Hull
    {
        Wood,
        Steel,
        Titanium
    }

    public struct Tank
    {
        public int liters;
    }

    public struct Engine
    {
        public float thrust;
        public Tank fuel;
    }

    public class Cargo
    {
        public string label = string.Empty;
        public float mass;
    }

    public sealed class HeavyCargo : Cargo
    {
        public int containers;
    }

    public sealed class Ship
    {
        public string name = "Nomad";
        public int crew = 3;
        public Hull hull = Hull.Steel;
        public Vector3 position = new Vector3(1, 2, 3);
        public Engine engine = new Engine { thrust = 10f, fuel = new Tank { liters = 50 } };
        public Cargo? cargo = new Cargo { label = "ore", mass = 2.5f };
        public List<Cargo> holds = new List<Cargo>
        {
            new Cargo { label = "ice", mass = 1f },
            new Cargo { label = "gas", mass = 4f }
        };
        public int[] codes = { 7, 8, 9 };
        public Dictionary<string, int> stock = new Dictionary<string, int> { ["iron"] = 4 };
        public Dictionary<int, string> slots = new Dictionary<int, string> { [1] = "laser" };
        public Action? onDock;

        public float Speed { get; set; } = 4f;
        public int Decks => 2;
    }

    public struct Orbit
    {
        public float radius;
        public float period;
    }

    public sealed class Module
    {
        public string label = string.Empty;
        public float radius;
    }

    public sealed class Station
    {
        public string title = "Ares";
        public Orbit orbit = new Orbit { radius = 120f, period = 3.5f };
        public List<Module> modules = new List<Module>
        {
            new Module { label = "dock", radius = 4f },
            new Module { label = "lab", radius = 2f }
        };
        public Station? sister;
    }
}
