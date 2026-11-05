#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Feeder.MCP.Editor.Tests.Tools
{
    public enum MatrixTestKind
    {
        Alpha,
        Beta,
    }

    [Serializable]
    public struct MatrixTestEntry
    {
        public string label;
        public int amount;
    }

    public sealed class MatrixTestData : ScriptableObject
    {
        [SerializeField] int hidden = 1;
        [SerializeField] MatrixTestKind kind;
        [SerializeField] Vector3 offset;
        [SerializeField] Color tint = Color.white;
        [SerializeField] UnityEngine.Object? link;
        [SerializeField] string[] names = Array.Empty<string>();
        [SerializeField] List<MatrixTestEntry> entries = new List<MatrixTestEntry>();

        public int Hidden => hidden;
        public MatrixTestKind Kind => kind;
        public Vector3 Offset => offset;
        public Color Tint => tint;
        public UnityEngine.Object? Link => link;
        public string[] Names => names;
        public List<MatrixTestEntry> Entries => entries;
    }
}
