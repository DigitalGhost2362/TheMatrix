---
name: matrix-console-get-logs
description: Retrieve Unity Editor logs from the MCP plugin's `LogCollector`, optionally filtered by log type or time window. Useful for debugging and monitoring Editor activity.
---

# Console / Get Logs

Retrieves Unity Editor logs. Useful for debugging and monitoring Unity Editor activity.

## Inputs

- `maxEntries` (default 100, minimum 1) — caps the size of the returned array.
- `logTypeFilter` — Unity `LogType` filter; `null` returns all severities.
- `includeStackTrace` (default `false`) — include stack-trace strings in each entry.
- `lastMinutes` (default 0) — when non-zero, only logs from the last N minutes are returned.

## How to Call

Call this tool through the MCP client connected to the local Matrix Bridge server.

Example input:
```json
{
  "maxEntries": 0,
  "logTypeFilter": {},
  "includeStackTrace": false,
  "lastMinutes": 0
}
```

> For complex input, keep the payload as valid JSON and send it through your connected MCP client.

Read the /feeder-mcp-initial-setup skill for local connection setup.

## Input

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `maxEntries` | `integer` | No | Maximum number of log entries to return. Minimum: 1. Default: 100 |
| `logTypeFilter` | `any` | No | Filter by log type. 'null' means All. |
| `includeStackTrace` | `boolean` | No | Include stack traces in the output. Default: false |
| `lastMinutes` | `integer` | No | Return logs from the last N minutes. If 0, returns all available logs. Default: 0 |

### Input JSON Schema

```json
{
  "type": "object",
  "properties": {
    "maxEntries": {
      "type": "integer"
    },
    "logTypeFilter": {
      "$ref": "#/$defs/UnityEngine.LogType"
    },
    "includeStackTrace": {
      "type": "boolean"
    },
    "lastMinutes": {
      "type": "integer"
    }
  },
  "$defs": {
    "UnityEngine.LogType": {
      "type": "string",
      "enum": [
        "Error",
        "Assert",
        "Warning",
        "Log",
        "Exception"
      ]
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
      "$ref": "#/$defs/Feeder.MCP.LogEntry-1"
    }
  },
  "$defs": {
    "Feeder.MCP.LogEntry": {
      "type": "object",
      "properties": {
        "LogType": {
          "type": "string",
          "enum": [
            "Error",
            "Assert",
            "Warning",
            "Log",
            "Exception"
          ]
        },
        "Message": {
          "type": "string"
        },
        "Timestamp": {
          "type": "string",
          "format": "date-time"
        },
        "StackTrace": {
          "type": "string"
        }
      },
      "required": [
        "LogType",
        "Timestamp"
      ]
    },
    "Feeder.MCP.LogEntry-1": {
      "type": "array",
      "items": {
        "$ref": "#/$defs/Feeder.MCP.LogEntry"
      }
    }
  },
  "required": [
    "result"
  ]
}
```

