#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Feeder.MCP.Editor.Utils
{
    /// <summary>Pure helpers for hashing, partial edits and atomic writes of script files.</summary>
    internal static class ScriptEdit
    {
        public static string Sha256(byte[] bytes)
        {
            using var sha = SHA256.Create();
            return string.Concat(sha.ComputeHash(bytes).Select(b => b.ToString("x2")));
        }

        public static bool HasBom(byte[] bytes) => bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;

        public static string ReadText(byte[] bytes)
        {
            var skip = HasBom(bytes) ? 3 : 0;
            return new UTF8Encoding(false).GetString(bytes, skip, bytes.Length - skip);
        }

        public static bool UsesCrlf(string text) => text.Contains("\r\n");

        public static string ToEol(string text, bool crlf)
        {
            var lf = text.Replace("\r\n", "\n");
            return crlf ? lf.Replace("\n", "\r\n") : lf;
        }

        /// <summary>Replaces <paramref name="oldText"/> by <paramref name="newText"/>; the match must be unique unless <paramref name="all"/>.</summary>
        public static bool Replace(string current, string oldText, string newText, bool all, out string result, out string? error)
        {
            var crlf = UsesCrlf(current);
            oldText = ToEol(oldText, crlf);
            newText = ToEol(newText, crlf);
            result = current;
            error = null;

            var count = 0;
            for (var at = current.IndexOf(oldText, StringComparison.Ordinal); at >= 0; at = current.IndexOf(oldText, at + oldText.Length, StringComparison.Ordinal))
                count++;

            if (count == 0)
            {
                error = "[anchor_not_found] oldText does not occur in the file. Read the file again and copy the text exactly.";
                return false;
            }
            if (count > 1 && !all)
            {
                error = $"[anchor_ambiguous count={count}] oldText occurs {count} times. Add surrounding lines to make it unique, or pass replaceAll=true.";
                return false;
            }

            result = current.Replace(oldText, newText);
            return true;
        }

        /// <summary>Writes through a temp file so a crash never leaves a half-written script.</summary>
        public static void WriteAtomic(string path, string text, bool bom)
        {
            var temp = path + ".mxtmp";
            try
            {
                File.WriteAllText(temp, text, new UTF8Encoding(bom));
                if (File.Exists(path))
                    File.Replace(temp, path, null);
                else
                    File.Move(temp, path);
            }
            finally
            {
                if (File.Exists(temp))
                    File.Delete(temp);
            }
        }

        static readonly Regex DiagnosticLine = new Regex(@"\((\d+),(\d+)\): error (\w+): (.*)$", RegexOptions.Compiled);

        /// <summary>Turns a compiler line into "line:col CODE message"; other lines pass through.</summary>
        public static string FormatDiagnostic(string raw)
        {
            var m = DiagnosticLine.Match(raw);
            return m.Success ? $"{m.Groups[1].Value}:{m.Groups[2].Value} {m.Groups[3].Value} {m.Groups[4].Value}" : raw;
        }
    }
}
