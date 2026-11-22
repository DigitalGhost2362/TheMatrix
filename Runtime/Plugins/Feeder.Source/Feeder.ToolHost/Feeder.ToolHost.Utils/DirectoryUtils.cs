using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Feeder.ToolHost.Utils
{
public static class DirectoryUtils
{
	public static void Delete(string path, bool recursive = true)
	{
		if (Directory.Exists(path))
		{
			Directory.Delete(path, recursive);
		}
	}

	public static void Copy(string sourceDir, string destinationDir, params string[] ignorePatterns)
	{
		Directory.CreateDirectory(destinationDir);
		Regex[] ignoreRegexes = ignorePatterns.Select((string pattern) => new Regex("^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$", RegexOptions.IgnoreCase)).ToArray();
		string[] files = Directory.GetFiles(sourceDir);
		foreach (string text in files)
		{
			if (!IsIgnored(text))
			{
				string destFileName = Path.Combine(destinationDir, Path.GetFileName(text));
				File.Copy(text, destFileName, overwrite: true);
			}
		}
		files = Directory.GetDirectories(sourceDir);
		foreach (string text2 in files)
		{
			if (!IsIgnored(text2))
			{
				string destinationDir2 = Path.Combine(destinationDir, Path.GetFileName(text2));
				Copy(text2, destinationDir2);
			}
		}
		bool IsIgnored(string path)
		{
			return ignoreRegexes.Any((Regex regex) => regex.IsMatch(path));
		}
	}
}
}
