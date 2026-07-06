namespace YouTracker.Core.Ai;

/// <summary>JSON schemas for structured AI outputs (strict: additionalProperties false, all fields required).</summary>
public static class AiSchemas
{
    public const string WorkLogDrafts = """
        {
          "type": "object",
          "properties": {
            "drafts": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "issueId": { "type": "string", "description": "Issue ID from the provided list, e.g. ALPHA-1238" },
                  "confidence": { "type": "string", "enum": ["high", "medium", "low"] },
                  "date": { "type": "string", "description": "yyyy-MM-dd" },
                  "minutes": { "type": "integer", "minimum": 1 },
                  "workTypeName": { "type": ["string", "null"], "description": "One of the provided work item types, or null" },
                  "comment": { "type": ["string", "null"] },
                  "reasoning": { "type": ["string", "null"] }
                },
                "required": ["issueId", "confidence", "date", "minutes", "workTypeName", "comment", "reasoning"],
                "additionalProperties": false
              }
            },
            "unmatched": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "text": { "type": "string" },
                  "reason": { "type": "string" }
                },
                "required": ["text", "reason"],
                "additionalProperties": false
              }
            }
          },
          "required": ["drafts", "unmatched"],
          "additionalProperties": false
        }
        """;

    public const string Triage = """
        {
          "type": "object",
          "properties": {
            "ranked": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "issueId": { "type": "string" },
                  "rank": { "type": "integer", "minimum": 1 },
                  "score": { "type": "integer", "minimum": 0, "maximum": 100 },
                  "reasons": { "type": "array", "items": { "type": "string" } }
                },
                "required": ["issueId", "rank", "score", "reasons"],
                "additionalProperties": false
              }
            },
            "focusSuggestion": { "type": "string" },
            "sprintSuggestions": {
              "type": "array",
              "description": "Tasks from the sprint pool matching the dev's focus; empty when no pool was provided",
              "items": {
                "type": "object",
                "properties": {
                  "issueId": { "type": "string" },
                  "rank": { "type": "integer", "minimum": 1 },
                  "score": { "type": "integer", "minimum": 0, "maximum": 100 },
                  "reasons": { "type": "array", "items": { "type": "string" } }
                },
                "required": ["issueId", "rank", "score", "reasons"],
                "additionalProperties": false
              }
            }
          },
          "required": ["ranked", "focusSuggestion", "sprintSuggestions"],
          "additionalProperties": false
        }
        """;
}
