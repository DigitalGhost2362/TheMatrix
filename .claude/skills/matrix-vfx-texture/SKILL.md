---
name: matrix-vfx-texture
description: Generate procedural particle textures (glow, circle, ring, spark, star, flare, smoke flipbook, tiling noise, trail, shockwave, polygon, lightning) into Assets/VFX/_Textures, or list existing ones with their recipes.
---

# VFX / Texture

# VFX / Texture

`action=create` writes `Assets/VFX/_Textures/<name>.png` (white RGB, shape in alpha, import settings set) and stores the recipe as its description. `action=list` prints every texture with its recipe. Reference a texture in a spec as `"material":"<name>/add"` (blend add|alpha|premul|mul); flipbook frames are wired automatically.

## How to Call

Call this tool through the MCP client connected to the local Matrix Bridge server.

Example input:
```json
{
  "action": "value",
  "name": "value",
  "recipe": "value",
  "preview": false
}
```

> For complex input, keep the payload as valid JSON and send it through your connected MCP client.

Read the /feeder-mcp-initial-setup skill for local connection setup.

## Input

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `action` | `string` | No | create | list |
| `name` | `string` | No | Texture name (file name without extension). |
| `recipe` | `string` | No | Recipe JSON, e.g. {"shape":"smoke","size":512,"frames":[4,4],"seed":3}. Shapes: glow circle ring spark star flare smoke noise trail shockwave polygon lightning. |
| `preview` | `boolean` | No | true = also return the texture image. |

### Input JSON Schema

```json
{
  "type": "object",
  "properties": {
    "action": {
      "type": "string"
    },
    "name": {
      "type": "string"
    },
    "recipe": {
      "type": "string"
    },
    "preview": {
      "type": "boolean"
    }
  }
}
```

## Output

This tool does not return structured output.

