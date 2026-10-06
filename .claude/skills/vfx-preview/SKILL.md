---
name: vfx-preview
description: "Deterministic preview of a VFX prefab: text stats (alive counts over time, per-emitter windows, fill/overdraw, warnings) and one labelled contact-sheet PNG of N time samples from a fixed camera."
---

# VFX / Preview

# VFX / Preview

Simulates with a fixed seed in an isolated layer, fits one camera to the union of all frames, renders `frames` cells into one grid. Pass the returned `frame=` value back to keep framing identical between iterations. `statsOnly=true` skips the image (a few hundred tokens instead of ~1k).

## How to Call

Call this tool through the MCP client connected to the local Matrix Bridge server.

Example input:
```json
{
  "path": "value",
  "frames": 0,
  "cols": 0,
  "cell": 0,
  "view": "value",
  "bg": "value",
  "frame": "value",
  "times": "value",
  "duration": 0,
  "seed": 0,
  "budget": 0,
  "statsOnly": false,
  "context": "value",
  "camera": "value"
}
```

> For complex input, keep the payload as valid JSON and send it through your connected MCP client.

Read the /feeder-mcp-initial-setup skill for local connection setup.

## Input

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `path` | `string` | Yes | Prefab path. |
| `frames` | `integer` | No | Number of time samples (1-16). |
| `cols` | `integer` | No | Grid columns. |
| `cell` | `integer` | No | Cell size in px (64-512). |
| `view` | `string` | No | front | back | side | top | iso |
| `bg` | `string` | No | Background hex, e.g. #1E1E1E (dark) or #9A9A9A (light, to check alpha blends). |
| `frame` | `string` | No | Fixed framing from a previous result, 'radius' or 'radius@x,y,z'. Empty = auto. |
| `times` | `string` | No | Explicit sample times in seconds, comma separated. Overrides frames. |
| `duration` | `number` | No | Seconds covered. 0 = auto from durations and lifetimes. |
| `seed` | `integer` | No | Random seed. |
| `budget` | `integer` | No | Particle budget for the warning. |
| `statsOnly` | `boolean` | No | true = text stats only, no image. |
| `context` | `string` | No | Optional model prefab path rendered with the effect parented at its root, e.g. the weapon an aura sits on. |
| `camera` | `string` | No | Optional camera name in the open scene. Renders the instance already placed in the scene through that camera, with its post-processing (bloom). Ignores view/bg/frame/context. |

### Input JSON Schema

```json
{
  "type": "object",
  "properties": {
    "path": {
      "type": "string"
    },
    "frames": {
      "type": "integer"
    },
    "cols": {
      "type": "integer"
    },
    "cell": {
      "type": "integer"
    },
    "view": {
      "type": "string"
    },
    "bg": {
      "type": "string"
    },
    "frame": {
      "type": "string"
    },
    "times": {
      "type": "string"
    },
    "duration": {
      "type": "number"
    },
    "seed": {
      "type": "integer"
    },
    "budget": {
      "type": "integer"
    },
    "statsOnly": {
      "type": "boolean"
    },
    "context": {
      "type": "string"
    },
    "camera": {
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

