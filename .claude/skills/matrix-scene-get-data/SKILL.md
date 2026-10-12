---
name: matrix-scene-get-data
description: Retrieve the list of root GameObjects in the specified opened scene (or the active scene when `openedSceneName` is empty). Supports token-saving path-scoped reads over the root-GameObjects array via `paths` or `viewQuery`. Use 'scene-list-opened' to enumerate scenes. For one specific GameObject use 'gameobject-find' instead.
---

# Scene / Get Data

This tool retrieves the list of root GameObjects in the specified scene. Use 'scene-list-opened' tool to get the list of all opened scenes.

## When to use / when not

- Use when you need the scene's root GameObject list or a hierarchy overview.
- Do NOT use to locate one specific GameObject — use 'gameobject-find' instead. For just the opened scene names use 'scene-list-opened'.

## Toggles (all default `false` to keep responses small)

- `includeRootGameObjects` — include root GameObjects in the scene data.
- `includeChildrenDepth` (default 3) — depth of the hierarchy to include.
- `includeBounds` — include 3D bounds for GameObjects.
- `includeData` — include serialized component data for GameObjects.

## Path-scoped reads (token-saving)

Supply `paths` to read only the listed fields/elements from the scene's root-GameObjects array via `Reflector.TryReadAt`, or `viewQuery` to navigate/filter the same array via `Reflector.View`. The result populates `Data` on the returned `SceneData`. These two parameters are mutually exclusive.

## Path syntax

`fieldName`, `nested/field`, `arrayField/[i]`, `dictField/[key]`. Leading `#/` is stripped. Example: `paths=['[0]/name']` reads the name of the first root GameObject.

## How to Call

Call this tool through the MCP client connected to the local Matrix Bridge server.

Example input:
```json
{
  "openedSceneName": "value",
  "includeRootGameObjects": false,
  "includeChildrenDepth": 0,
  "includeBounds": false,
  "includeData": false,
  "paths": [],
  "viewQuery": {}
}
```

> For complex input, keep the payload as valid JSON and send it through your connected MCP client.

Read the /feeder-mcp-initial-setup skill for local connection setup.

## Input

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `openedSceneName` | `string` | No | Name of the opened scene. If empty or null, the active scene will be used. |
| `includeRootGameObjects` | `boolean` | No | If true, includes root GameObjects in the scene data. |
| `includeChildrenDepth` | `integer` | No | Determines the depth of the hierarchy to include. |
| `includeBounds` | `boolean` | No | If true, includes bounding box information for GameObjects. |
| `includeData` | `boolean` | No | If true, includes component data for GameObjects. |
| `paths` | `any` | No | Optional. List of paths to read individually via Reflector.TryReadAt against the scene's root-GameObjects array. Path syntax: 'fieldName', '[i]/field', '[i]/component/[j]/property'. Mutually exclusive with 'viewQuery'. |
| `viewQuery` | `any` | No | Optional. View-query filter routed through Reflector.View on the scene's root-GameObjects array. Mutually exclusive with 'paths'. |

### Input JSON Schema

```json
{
  "type": "object",
  "properties": {
    "openedSceneName": {
      "type": "string"
    },
    "includeRootGameObjects": {
      "type": "boolean"
    },
    "includeChildrenDepth": {
      "type": "integer"
    },
    "includeBounds": {
      "type": "boolean"
    },
    "includeData": {
      "type": "boolean"
    },
    "paths": {
      "$ref": "#/$defs/System.Collections.Generic.List(System.String)"
    },
    "viewQuery": {
      "$ref": "#/$defs/Feeder.Reflect.Model.ViewQuery"
    }
  },
  "$defs": {
    "System.Collections.Generic.List(System.String)": {
      "type": "array",
      "items": {
        "type": "string"
      }
    },
    "System.Type": {
      "type": "string"
    },
    "Feeder.Reflect.Model.ViewQuery": {
      "type": "object",
      "properties": {
        "Path": {
          "type": "string",
          "description": "Navigate to this path first, then serialize only that subtree. Path segments are separated by '/'. Use '[i]' for array/list index (e.g. 'users/[2]/name') and '[key]' for dictionary entry (e.g. 'config/[timeout]'). A leading '#/' is stripped automatically. Examples: 'admin/name', 'users/[0]/email', 'config/[timeout]'. Leave null to start from the root object."
        },
        "NamePattern": {
          "type": "string",
          "description": "Case-insensitive .NET regex pattern matched against field and property names. Only branches containing at least one match are kept in the result tree. Examples: 'orbitRadius' (exact name), 'orbit.*' (prefix match), 'radius|speed' (either name). When nothing matches, the root envelope is returned with empty fields/props. Leave null to return all fields and properties without filtering."
        },
        "MaxDepth": {
          "type": "integer",
          "description": "Maximum nesting depth of the returned serialized tree. 0 = root type name and value only — no nested fields or properties. 1 = one level of fields/props visible, their children stripped. 2 = two levels visible, and so on. Leave null (default) for unlimited depth."
        },
        "TypeFilter": {
          "$ref": "#/$defs/System.Type",
          "description": "When set, prunes the result tree to members whose runtime type is assignable to this type. Non-matching branches are removed; the root envelope is always preserved. Examples: typeof(float) keeps only float fields, typeof(IEnumerable) keeps only collections. Leave null to include members of any type."
        }
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
      "$ref": "#/$defs/Feeder.MCP.Data.SceneData",
      "description": "Scene reference. Used to find a Scene."
    }
  },
  "$defs": {
    "System.Collections.Generic.List(Feeder.MCP.Data.GameObjectData)": {
      "type": "array",
      "items": {
        "$ref": "#/$defs/Feeder.MCP.Data.GameObjectData"
      }
    },
    "Feeder.MCP.Data.GameObjectData": {
      "type": "object",
      "properties": {
        "Reference": {
          "$ref": "#/$defs/Feeder.MCP.Data.GameObjectRef",
          "description": "Find GameObject in opened Prefab or in the active Scene."
        },
        "Data": {
          "$ref": "#/$defs/Feeder.Reflect.Model.SerializedMember",
          "description": "GameObject editable data (tag, layer, etc)."
        },
        "Bounds": {
          "$ref": "#/$defs/UnityEngine.Bounds",
          "description": "Bounds of the GameObject."
        },
        "Hierarchy": {
          "$ref": "#/$defs/Feeder.MCP.Data.GameObjectMetadata",
          "description": "Hierarchy metadata of the GameObject."
        },
        "Components": {
          "$ref": "#/$defs/Feeder.MCP.Data.ComponentDataShallow-1",
          "description": "Attached components shallow data of the GameObject (Read-only, use Component modification tool for modification)."
        }
      }
    },
    "Feeder.MCP.Data.GameObjectRef": {
      "type": "object",
      "properties": {
        "instanceID": {
          "type": "integer",
          "description": "instanceID of the UnityEngine.Object. If it is '0' and 'path', 'name', 'assetPath' and 'assetGuid' is not provided, empty or null, then it will be used as 'null'. Priority: 1 (Recommended)"
        },
        "path": {
          "type": "string",
          "description": "Path of a GameObject in the hierarchy Sample 'character/hand/finger/particle'. Priority: 2."
        },
        "name": {
          "type": "string",
          "description": "Name of a GameObject in hierarchy. Priority: 3."
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
      "description": "Find GameObject in opened Prefab or in the active Scene."
    },
    "System.Type": {
      "type": "string"
    },
    "Feeder.Reflect.Model.SerializedMember": {
      "type": "object",
      "properties": {
        "typeName": {
          "type": "string",
          "description": "Full type name. Eg: 'System.String', 'System.Int32', 'UnityEngine.Vector3', etc."
        },
        "name": {
          "type": "string",
          "description": "Object name."
        },
        "value": {
          "description": "Value of the object, serialized as a non stringified JSON element. Can be null if the value is not set. Can be default value if the value is an empty object or array json."
        },
        "fields": {
          "type": "array",
          "items": {
            "$ref": "#/$defs/Feeder.Reflect.Model.SerializedMember",
            "description": "Nested field value."
          },
          "description": "Fields of the object, serialized as a list of 'SerializedMember'."
        },
        "props": {
          "type": "array",
          "items": {
            "$ref": "#/$defs/Feeder.Reflect.Model.SerializedMember",
            "description": "Nested property value."
          },
          "description": "Properties of the object, serialized as a list of 'SerializedMember'."
        }
      },
      "required": [
        "typeName"
      ],
      "additionalProperties": false
    },
    "Feeder.Reflect.Model.SerializedMemberList": {
      "type": "array",
      "items": {
        "$ref": "#/$defs/Feeder.Reflect.Model.SerializedMember"
      }
    },
    "UnityEngine.Bounds": {
      "type": "object",
      "properties": {
        "center": {
          "type": "object",
          "properties": {
            "x": {
              "type": "number"
            },
            "y": {
              "type": "number"
            },
            "z": {
              "type": "number"
            }
          },
          "required": [
            "x",
            "y",
            "z"
          ]
        },
        "size": {
          "type": "object",
          "properties": {
            "x": {
              "type": "number"
            },
            "y": {
              "type": "number"
            },
            "z": {
              "type": "number"
            }
          },
          "required": [
            "x",
            "y",
            "z"
          ]
        }
      },
      "required": [
        "center",
        "size"
      ],
      "additionalProperties": false
    },
    "Feeder.MCP.Data.GameObjectMetadata": {
      "type": "object",
      "properties": {
        "instanceID": {
          "type": "integer"
        },
        "path": {
          "type": "string"
        },
        "name": {
          "type": "string"
        },
        "sceneName": {
          "type": "string"
        },
        "tag": {
          "type": "string"
        },
        "activeSelf": {
          "type": "boolean"
        },
        "activeInHierarchy": {
          "type": "boolean"
        },
        "children": {
          "$ref": "#/$defs/System.Collections.Generic.List(Feeder.MCP.Data.GameObjectMetadata)"
        }
      },
      "required": [
        "instanceID",
        "activeSelf",
        "activeInHierarchy"
      ]
    },
    "System.Collections.Generic.List(Feeder.MCP.Data.GameObjectMetadata)": {
      "type": "array",
      "items": {
        "$ref": "#/$defs/Feeder.MCP.Data.GameObjectMetadata"
      }
    },
    "Feeder.MCP.Data.ComponentDataShallow-1": {
      "type": "array",
      "items": {
        "$ref": "#/$defs/Feeder.MCP.Data.ComponentDataShallow"
      }
    },
    "Feeder.MCP.Data.ComponentDataShallow": {
      "type": "object",
      "properties": {
        "instanceID": {
          "type": "integer"
        },
        "typeName": {
          "type": "string"
        },
        "isEnabled": {
          "type": "string",
          "enum": [
            "False",
            "True",
            "NA"
          ]
        }
      },
      "required": [
        "instanceID",
        "isEnabled"
      ]
    },
    "Feeder.MCP.Data.SceneData": {
      "type": "object",
      "properties": {
        "RootGameObjects": {
          "$ref": "#/$defs/System.Collections.Generic.List(Feeder.MCP.Data.GameObjectData)"
        },
        "Data": {
          "$ref": "#/$defs/Feeder.Reflect.Model.SerializedMember",
          "description": "Path-scoped read or view-query result, populated when 'paths' or 'viewQuery' is supplied to the scene-get-data tool. Null otherwise."
        },
        "Name": {
          "type": "string"
        },
        "IsLoaded": {
          "type": "boolean"
        },
        "IsDirty": {
          "type": "boolean"
        },
        "IsSubScene": {
          "type": "boolean"
        },
        "IsValidScene": {
          "type": "boolean",
          "description": "Whether this is a valid Scene. A Scene may be invalid if, for example, you tried to open a Scene that does not exist. In this case, the Scene returned from EditorSceneManager.OpenScene would return False for IsValid."
        },
        "RootCount": {
          "type": "integer"
        },
        "path": {
          "type": "string",
          "description": "Path to the Scene within the project. Starts with 'Assets/'"
        },
        "buildIndex": {
          "type": "integer",
          "description": "Build index of the Scene in the Build Settings."
        },
        "instanceID": {
          "type": "integer",
          "description": "instanceID of the UnityEngine.Object. If this is '0', then it will be used as 'null'."
        }
      },
      "required": [
        "IsLoaded",
        "IsDirty",
        "IsSubScene",
        "IsValidScene",
        "RootCount",
        "buildIndex",
        "instanceID"
      ],
      "description": "Scene reference. Used to find a Scene."
    }
  },
  "required": [
    "result"
  ]
}
```

