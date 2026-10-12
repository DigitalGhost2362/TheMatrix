---
name: matrix-vfx-apply
description: Create or patch a ParticleSystem VFX prefab from a compact JSON spec (emitters addressed by name, only changed fields). Saves a history version and returns text stats, optionally a time-lapse contact sheet.
---

# VFX / Apply spec

# VFX / Apply spec

Creates the prefab when missing, otherwise patches it: emitters are matched by `name`, missing ones are created, `{"name":"X","remove":true}` deletes. `replace=true` rebuilds every emitter from the spec. Every call stores a history version (see `vfx-history`).

`preview`: `stats` (default, text only, cheapest), `sheet` (adds the contact sheet image), `none`.

Full spec reference and the iteration loop live in the `matrix-vfx` skill.

## How to Call

Call this tool through the MCP client connected to the local Matrix Bridge server.

Example input:
```json
{
  "path": "value",
  "spec": "value",
  "replace": false,
  "note": "value",
  "preview": "value"
}
```

> For complex input, keep the payload as valid JSON and send it through your connected MCP client.

Read the /feeder-mcp-initial-setup skill for local connection setup.

## Input

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `path` | `string` | Yes | Prefab path, e.g. Assets/VFX/HitSpark.prefab. Created if missing. |
| `spec` | `string` | Yes | JSON: {"defaults":{...},"root":{...},"emitters":[{"name":"Core","material":"vfx_glow/add","main":{...},"emission":{...},...}]}. Only changed fields. |
| `replace` | `boolean` | No | true = rebuild all emitters from spec; false = patch by name. |
| `note` | `string` | No | Short note stored with this history version. |
| `preview` | `string` | No | stats | sheet | none |

### Input JSON Schema

```json
{
  "type": "object",
  "properties": {
    "path": {
      "type": "string"
    },
    "spec": {
      "type": "string"
    },
    "replace": {
      "type": "boolean"
    },
    "note": {
      "type": "string"
    },
    "preview": {
      "type": "string"
    }
  },
  "required": [
    "path",
    "spec"
  ]
}
```

## Output

This tool does not return structured output.

