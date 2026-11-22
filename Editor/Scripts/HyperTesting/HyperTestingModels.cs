#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Feeder.MCP.Editor.HyperTesting
{
    [Serializable]
    public class HyperTestingConfig
    {
        public int maxClones = 3;
        public string clonesRoot = string.Empty;
        public int basePort = 27100;
        public int ramReserveMb = 4096;
        public int ramPerCloneEstimateMb = 4096;
        public int maxParallelCompiles = 1;
        public int compileTimeoutSec = 300;
        public int jobWorkerCount = 2;
        public bool belowNormalPriority = true;
        public int playTargetFrameRate = 30;
        public bool playLowestQuality = true;
        public bool isolatePlayerPrefs = true;
        public bool closeSceneViews = true;
        public int ramSampleIntervalSec = 30;
        public int cloneImportWorkers = 0;
        public int vramOneCloneMaxMb = 4096;

        public static HyperTestingConfig Load()
        {
            var path = HyperTestingPaths.ConfigFile;
            var config = File.Exists(path) ? JsonUtility.FromJson<HyperTestingConfig>(File.ReadAllText(path)) : null;
            return config ?? new HyperTestingConfig();
        }

        public void SaveIfMissing()
        {
            if (!File.Exists(HyperTestingPaths.ConfigFile))
                File.WriteAllText(HyperTestingPaths.ConfigFile, JsonUtility.ToJson(this, true));
        }

        public string ResolvedClonesRoot(string projectRoot)
        {
            if (!string.IsNullOrWhiteSpace(clonesRoot))
                return Path.GetFullPath(clonesRoot);
            var project = new DirectoryInfo(projectRoot);
            return Path.Combine(project.Parent!.FullName, project.Name + ".HyperClones");
        }
    }

    [Serializable]
    public class HyperCloneMarker
    {
        public int index;
        public int port;
        public string originRoot = string.Empty;
        public string originDataPath = string.Empty;
        public string cloneRoot = string.Empty;
        public string createdUtc = string.Empty;
        public string packageVersion = string.Empty;
        public int playTargetFrameRate = 30;
        public bool playLowestQuality = true;
        public bool isolatePlayerPrefs = true;
        public bool closeSceneViews = true;
        public int importWorkers = 0;
    }

    [Serializable]
    public class HyperCloneStatus
    {
        public int index;
        public int pid;
        public long writtenUnix;
        public bool runnerActive;
        public string runnerProblem = string.Empty;
        public bool isPlaying;
        public bool isCompiling;
        public bool isUpdating;
        public bool compileFailed;
        public long workingSetMb;
        public long peakWorkingSetMb;
        public long commitMb;
        public int workers;
        public long workersCommitMb;
        public int blockedWrites;
        public int createdAssetCount;
        public string[] createdAssets = Array.Empty<string>();
        public string lastCommandId = string.Empty;
        public string lastCommandResult = string.Empty;
    }

    [Serializable]
    public class HyperCloneCommand
    {
        public string id = string.Empty;
        public string command = string.Empty;
    }

    [Serializable]
    public class HyperPoolEntry
    {
        public int index;
        public int port;
        public int pid;
        public string root = string.Empty;
        public string state = string.Empty;
        public string note = string.Empty;
    }

    [Serializable]
    public class HyperPoolState
    {
        static readonly object _fileLock = new object();

        public List<HyperPoolEntry> clones = new List<HyperPoolEntry>();

        public static HyperPoolState Load()
        {
            lock (_fileLock)
            {
                var path = HyperTestingPaths.PoolFile;
                var state = File.Exists(path) ? JsonUtility.FromJson<HyperPoolState>(File.ReadAllText(path)) : null;
                return state ?? new HyperPoolState();
            }
        }

        public void Save()
        {
            lock (_fileLock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(HyperTestingPaths.PoolFile)!);
                File.WriteAllText(HyperTestingPaths.PoolFile, JsonUtility.ToJson(this, true));
            }
        }

        public HyperPoolEntry? Find(int index) => clones.Find(c => c.index == index);
    }

    [UnityEditor.InitializeOnLoad]
    public static class HyperTestingPaths
    {
        public const string MarkerFileName = ".matrix-hyper-clone.json";
        // not "matrix-hyper-testing": that folder belongs to the skill of the hyper-testing tool
        public const string SkillId = "matrix-hyper-testing-guide";
        public const string OldSkillId = "matrix-hyper-testing";
        public const string AgentPrefix = "hyper-playtester-";

        // cached on the main thread: Application.dataPath cannot be read from pool worker threads
        public static readonly string ProjectRoot = Path.GetDirectoryName(Application.dataPath)!;
        public static readonly string DataPath = Application.dataPath;
        public static string Workspace => Path.Combine(ProjectRoot, "HyperTesting");
        public static string ConfigFile => Path.Combine(Workspace, "config.json");
        public static string Journal => Path.Combine(Workspace, "Journal");
        public static string Runs => Path.Combine(Workspace, "Runs");
        public static string RamCsv => Path.Combine(Journal, "ram-samples.csv");
        public static string RamNotes => Path.Combine(Journal, "ram-observations.md");
        public static string CloneIssues => Path.Combine(Journal, "clone-issues.md");
        public static string PoolFile => Path.Combine(ProjectRoot, "Library", "MatrixHyperTesting", "pool.json");
        public static string GitBaselineFile => Path.Combine(ProjectRoot, "Library", "MatrixHyperTesting", "origin-git-baseline.txt");
        public static string AgentsFolder => Path.Combine(ProjectRoot, ".claude", "agents");
        public static string SkillFolder => Path.Combine(MatrixPluginEditor.SkillsRootFolderAbsolutePath, SkillId);

        public static string ChannelFolder(string cloneRoot) => Path.Combine(cloneRoot, "Temp", "MatrixHyper");
        public static string StatusFile(string cloneRoot) => Path.Combine(ChannelFolder(cloneRoot), "status.json");
        public static string CommandFile(string cloneRoot) => Path.Combine(ChannelFolder(cloneRoot), "command.json");
    }
}
