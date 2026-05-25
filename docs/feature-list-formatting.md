# List Formatting Mode

## Purpose
Convert spoken enumerations into well-structured markdown lists—numbered, bulleted, or task-lists—automatically during the formatting stage.

## Problem Solved
Users frequently dictate lists of items, action items, or agendas. Without explicit handling, chat models may output lists as dense paragraphs or apply inconsistent formatting. A dedicated mode ensures that "1. Apples 2. Bananas 3. Oranges" becomes a clean markdown list every time.

## How It Works
1. **System Prompt Specialization:** The prompt instructs the model to detect spoken list patterns and rewrite them as markdown lists. It specifies:
   - Use `- ` for unordered bullets.
   - Use `1. `, `2. `, etc. for ordered lists.
   - Use `- [ ] ` for task lists if the user implies checkboxes (e.g., "to-do: call John").
   - Preserve indentation for nested lists when the speaker says "sub-item" or "under that."
2. **Regex Safety Net:** A post-processing pass scans the model output. If the raw transcription contained obvious numeric list markers (`1.`, `2.`, `3.`) but the model failed to format them as a list, the safety net inserts markdown formatting automatically.
3. **Multi-line Handling:** The formatter splits the output on newlines and ensures each list item is on its own line.

### Examples
| Raw Dictation | List Mode Output |
|---------------|------------------|
| "Grocery list 1. Milk 2. Eggs 3. Bread" | `Grocery list\n\n1. Milk\n2. Eggs\n3. Bread` |
| "Action items call John email the team review the PR" | `- [ ] Call John\n- [ ] Email the team\n- [ ] Review the PR` |
| "Pros fast cheap cons unreliable hard to maintain" | `**Pros**\n\n- Fast\n- Cheap\n\n**Cons**\n\n- Unreliable\n- Hard to maintain` |

## Integration
- Available as a selectable mode alongside Standard, Formal, and Raw.
- Composable with **Spoken Punctuation** (user can say "new line" to force item breaks even if the model misses them).

## User Benefit
- Eliminates manual reformatting of dictated agendas, task lists, and pros/cons.
- Makes Prompter viable for structured note-taking in tools like Notion, Obsidian, and GitHub.
