---
name: vfx-history
description: List, score or restore saved versions of a VFX prefab. Every vfx-apply stores one; manual Inspector edits are captured before the next apply.
---

# VFX / History

# VFX / History

`action=list` shows `v<id> time score note`. `action=score` stores a 0-1 score (and note) on a version so the best one can be picked. `action=restore` rebuilds the prefab from a version and records the restore as a new version.

## How to Call

Call this tool through the MCP client connected to the local Matrix Bridge server.

Example input:
```json
{
  "path": "value",
  "action": "value",
  "id": 0,
  "score": 0,
  "note": "value"
}
```

> For complex input, keep the payload as valid JSON and send it through your connected MCP client.

Read the /feeder-mcp-initial-setup skill for local connection setup.

## Input

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `path` | `string` | Yes | Prefab path. |
| `action` | `string` | No | list | score | restore |
| `id` | `integer` | No | Version id for score/restore. |
| `score` | `number` | No | Score 0-1 for action=score. |
| `note` | `string` | No | Note for action=score. |

### Input JSON Schema

```json
{
  "type": "object",
  "properties": {
    "path": {
      "type": "string"
    },
    "action": {
      "type": "string"
    },
    "id": {
      "type": "integer"
    },
    "score": {
      "type": "number"
    },
    "note": {
      "type": "string"
    }
  },
  "required": [
    "path"
  ]
}
```

## Output

This tool does not return structured output.

