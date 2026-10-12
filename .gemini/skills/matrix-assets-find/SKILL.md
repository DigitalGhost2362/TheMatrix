---
name: matrix-assets-find
description: "Search the Unity asset database using a search filter string. The filter accepts names, labels (`l:`), types (`t:`), AssetBundles (`b:`), areas (`a:`), and globs (`glob:`). See the body for the full filter syntax. For scene GameObjects use 'gameobject-find' instead."
---

# Assets / Find

Search the asset database using the search filter string. Allows you to search for Assets. The string argument can provide names, labels or types (classnames).

## When to use / when not

- Use to locate project assets by name, label, type, bundle, area or glob.
- Do NOT use to locate scene GameObjects — use 'gameobject-find'. Once you have an asset, read its data with 'assets-get-data'.

## Filter syntax

- **Name** — filter assets by their filename (without extension). Words separated by whitespace are treated as separate name searches.
- **Labels (`l:`)** — assets can have labels attached. Use `l:` before each label.
- **Types (`t:`)** — find assets based on explicitly identified types. Available types include AnimationClip, AudioClip, AudioMixer, ComputeShader, Font, GUISkin, Material, Mesh, Model, PhysicMaterial, Prefab, Scene, Script, Shader, Sprite, Texture, VideoClip, VisualEffectAsset, VisualEffectSubgraph.
- **AssetBundles (`b:`)** — find assets which are part of an Asset bundle.
- **Area (`a:`)** — find assets in a specific area. Valid values: `all`, `assets`, `packages`.
- **Globbing (`glob:`)** — use globbing to match specific rules.

Searching is case-insensitive. Use `searchInFolders` to restrict the search scope. `maxResults` caps the returned list (default 10) — results beyond it are truncated.

## How to Call

Call this tool through the MCP client connected to the local Matrix Bridge server.

Example input:
```json
{
  "filter": "value",
  "searchInFolders": {},
  "maxResults": 0
}
```

> For complex input, keep the payload as valid JSON and send it through your connected MCP client.

Read the /feeder-mcp-initial-setup skill for local connection setup.

## Input

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `filter` | `string` | No | The filter string can contain search data. Could be empty. Name: Filter assets by their filename (without extension). Words separated by whitespace are treated as separate name searches. Labels (l:): Assets can have labels attached to them. Use 'l:' before each label. Types (t:): Find assets based on explicitly identified types. Use 't:' keyword. Available types: AnimationClip, AudioClip, AudioMixer, ComputeShader, Font, GUISkin, Material, Mesh, Model, PhysicMaterial, Prefab, Scene, Script, Shader, Sprite, Texture, VideoClip, VisualEffectAsset, VisualEffectSubgraph. AssetBundles (b:): Find assets which are part of an Asset bundle. Area (a:): Find assets in a specific area. Valid values are 'all', 'assets', and 'packages'. Globbing (glob:): Use globbing to match specific rules. Note: Searching is case insensitive. |
| `searchInFolders` | `any` | No | The folders where the search will start. If null, the search will be performed in all folders. |
| `maxResults` | `integer` | No | Maximum number of assets to return. If the number of found assets exceeds this limit, the result will be truncated. |

### Input JSON Schema

```json
{
  "type": "object",
  "properties": {
    "filter": {
      "type": "string"
    },
    "searchInFolders": {
      "$ref": "#/$defs/System.String-1"
    },
    "maxResults": {
      "type": "integer"
    }
  },
  "$defs": {
    "System.String-1": {
      "type": "array",
      "items": {
        "type": "string"
      }
    }
  }
}
```

## Output

### Output JSON Schema

```json
{
  "type": "object",
  "properties": {
    "result": {
      "$ref": "#/$defs/System.Collections.Generic.List(Feeder.MCP.Data.AssetObjectRef)"
    }
  },
  "$defs": {
    "Feeder.MCP.Data.AssetObjectRef": {
      "type": "object",
      "properties": {
        "instanceID": {
          "type": "integer",
          "description": "instanceID of the UnityEngine.Object. If this is '0' and 'assetPath' and 'assetGuid' is not provided, empty or null, then it will be used as 'null'."
        },
        "assetType": {
          "$ref": "#/$defs/System.Type",
          "description": "Type of the asset."
        },
        "assetPath": {
          "type": "string",
          "description": "Path to the asset within the project. Starts with 'Assets/'"
        },
        "assetGuid": {
          "type": "string",
          "description": "Unique identifier for the asset."
        }
      },
      "required": [
        "instanceID"
      ],
      "description": "Reference to UnityEngine.Object asset instance. It could be Material, ScriptableObject, Prefab, and any other Asset. Anything located in the Assets and Packages folders."
    },
    "System.Type": {
      "type": "string"
    },
    "System.Collections.Generic.List(Feeder.MCP.Data.AssetObjectRef)": {
      "type": "array",
      "items": {
        "$ref": "#/$defs/Feeder.MCP.Data.AssetObjectRef",
        "description": "Reference to UnityEngine.Object asset instance. It could be Material, ScriptableObject, Prefab, and any other Asset. Anything located in the Assets and Packages folders."
      }
    }
  },
  "required": [
    "result"
  ]
}
```

