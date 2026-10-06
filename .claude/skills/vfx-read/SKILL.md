---
name: vfx-read
description: "Read a VFX prefab as compact JSON: only values that differ from the baseline emitter. Use it to see manual Inspector edits before patching."
---

# VFX / Read spec

# VFX / Read spec

Returns the same format `vfx-apply` accepts, minified, with defaults omitted. Rotations are in degrees. Read before patching an effect a human may have touched.

## How to Call

Call this tool through the MCP client connected to the local Matrix Bridge server.

Example input:
```json
{
  "path": "value"
}
```

> For complex input, keep the payload as valid JSON and send it through your connected MCP client.

Read the /feeder-mcp-initial-setup skill for local connection setup.

## Input

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `path` | `string` | Yes | Prefab path. |

### Input JSON Schema

```json
{
  "type": "object",
  "properties": {
    "path": {
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

