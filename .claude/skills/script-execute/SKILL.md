---
name: script-execute
description: Compiles and executes C# code dynamically using Roslyn. Supports a full-code mode (default) and a body-only mode — see the skill body for the difference and for how to pass Unity object references as parameters.
---

# Script / Execute

## Modes

- **Full code mode** (default, `isMethodBody=false`): the `csharpCode` argument must define a complete class with a static method (no top-level statements).
- **Body-only mode** (`isMethodBody=true`): provide only the method body statements. The tool auto-generates the usings, class, and method header.

## Passing Unity objects as parameters

Unity objects (`GameObject`, `Component`, etc.) can be passed as parameters using their `Ref` types (`GameObjectRef`, `ComponentRef`, etc.) or directly by type:

- `UnityEngine.GameObject` — resolves an actual GameObject from value `{"instanceID": N}`, `{"name": "..."}`, or `{"path": "..."}`.
- `UnityEngine.Component` (or any component subtype) — resolves from `{"instanceID": N}`.
- `AIGD.GameObjectRef` — passes a `GameObjectRef` POCO directly; the method body calls `goRef.FindGameObject()` to resolve it.
- `AIGD.ComponentRef` — passes a `ComponentRef` POCO.
- `AIGD.ObjectRef` — passes a base `ObjectRef` POCO.

## How to Call

Call this tool through the MCP client connected to the local Matrix Bridge server.

Example input:
```json
{
  "csharpCode": "value",
  "className": "value",
  "methodName": "value",
  "parameters": {},
  "isMethodBody": false
}
```

> For complex input, keep the payload as valid JSON and send it through your connected MCP client.

Read the /feeder-mcp-initial-setup skill for local connection setup.

## Input

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `csharpCode` | `string` | Yes | C# code to compile and execute. In full code mode (default, isMethodBody=false): must define a complete class with a static method. Example: 'using UnityEngine; public class Script { public static void Main() { Debug.Log("Hello"); } }'. Do NOT use top-level statements. In body-only mode (isMethodBody=true): provide only the method body statements. The tool auto-generates usings, class, and method header. Example body: 'go.SetActive(false);'. Custom helper classes can still be defined inline in the body-only string after the main logic, but for complex additional class definitions use full code mode instead. |
| `className` | `string` | No | The name of the class containing the method to execute. In body-only mode this becomes the generated class name. |
| `methodName` | `string` | No | The name of the method to execute. Must be a static method. In body-only mode this becomes the generated method name. |
| `parameters` | `any` | No | Serialized parameters to pass to the method. Each entry must specify 'name' and 'typeName'. Supported parameter types include primitives, strings, and Unity object references: - 'UnityEngine.GameObject': resolves an actual GameObject from value '{"instanceID": N}', '{"name": "..."}', or '{"path": "..."}'. - 'UnityEngine.Component' (or any component subtype): resolves from '{"instanceID": N}'. - 'AIGD.GameObjectRef': passes a GameObjectRef POCO directly;   the method body calls goRef.FindGameObject() to resolve it. - 'AIGD.ComponentRef': passes a ComponentRef POCO. - 'AIGD.ObjectRef': passes a base ObjectRef POCO. If the method does not require parameters, leave this empty. |
| `isMethodBody` | `boolean` | No | When true, 'csharpCode' is treated as just the method body. The tool auto-generates standard using directives (System, UnityEngine, AIGD, Feeder.MCP.Runtime.Extensions, UnityEditor), the class definition, and the method signature (void return type). Parameters from the 'parameters' list are automatically added to the method signature using their typeName and name. When false (default), 'csharpCode' must be a complete C# compilation unit with class and method definitions. |

### Input JSON Schema

```json
{
  "type": "object",
  "properties": {
    "csharpCode": {
      "type": "string"
    },
    "className": {
      "type": "string"
    },
    "methodName": {
      "type": "string"
    },
    "parameters": {
      "$ref": "#/$defs/Feeder.ReflectorNet.Model.SerializedMemberList"
    },
    "isMethodBody": {
      "type": "boolean"
    }
  },
  "$defs": {
    "Feeder.ReflectorNet.Model.SerializedMemberList": {
      "type": "array",
      "items": {
        "$ref": "#/$defs/Feeder.ReflectorNet.Model.SerializedMember"
      }
    },
    "Feeder.ReflectorNet.Model.SerializedMember": {
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
            "$ref": "#/$defs/Feeder.ReflectorNet.Model.SerializedMember",
            "description": "Nested field value."
          },
          "description": "Fields of the object, serialized as a list of 'SerializedMember'."
        },
        "props": {
          "type": "array",
          "items": {
            "$ref": "#/$defs/Feeder.ReflectorNet.Model.SerializedMember",
            "description": "Nested property value."
          },
          "description": "Properties of the object, serialized as a list of 'SerializedMember'."
        }
      },
      "required": [
        "typeName"
      ],
      "additionalProperties": false
    }
  },
  "required": [
    "csharpCode"
  ]
}
```

## Output

### Output JSON Schema

```json
{
  "type": "object",
  "properties": {
    "result": {
      "$ref": "#/$defs/Feeder.ReflectorNet.Model.SerializedMember"
    }
  },
  "$defs": {
    "Feeder.ReflectorNet.Model.SerializedMemberList": {
      "type": "array",
      "items": {
        "$ref": "#/$defs/Feeder.ReflectorNet.Model.SerializedMember"
      }
    },
    "Feeder.ReflectorNet.Model.SerializedMember": {
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
            "$ref": "#/$defs/Feeder.ReflectorNet.Model.SerializedMember",
            "description": "Nested field value."
          },
          "description": "Fields of the object, serialized as a list of 'SerializedMember'."
        },
        "props": {
          "type": "array",
          "items": {
            "$ref": "#/$defs/Feeder.ReflectorNet.Model.SerializedMember",
            "description": "Nested property value."
          },
          "description": "Properties of the object, serialized as a list of 'SerializedMember'."
        }
      },
      "required": [
        "typeName"
      ],
      "additionalProperties": false
    }
  },
  "required": [
    "result"
  ]
}
```

