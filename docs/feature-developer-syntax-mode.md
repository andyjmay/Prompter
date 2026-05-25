# Developer / Syntax-Aware Mode

## Purpose
Optimize transcription and formatting for software development workflows. Preserve code identifiers, file paths, CLI commands, brackets, and casing exactly as spoken, minimizing the risk of a chat model "helpfully" rewriting technical syntax.

## Problem Solved
General-purpose dictation tools treat code as English prose. They lowercase identifiers, insert spaces into camelCase, rewrite file paths, and turn CLI flags into sentences. For developers using voice in IDEs (VS Code, Cursor, Windsurf), this creates more work than typing.

## How It Works
1. **System Prompt Specialization:** The prompt includes strict constraints:
   - Treat camelCase, PascalCase, snake_case, and kebab-case as immutable.
   - Do not insert spaces around dots in file paths (`user.controller.ts` stays exact).
   - Preserve all brackets, parentheses, braces, angle brackets, and quotes.
   - Do not spell out symbols: `=>` stays `=>`, `===` stays `===`, `!=` stays `!=`.
   - CLI commands (e.g., `git commit -m "fix"`) must remain verbatim.
2. **Post-Processing Safeguards:**
   - A regex pass restores common code patterns that the model may have mangled (e.g., `git commit -m` instead of `git commit dash m`).
   - File path detection: patterns matching `*.ts`, `*.py`, `*.cs`, etc. are locked in place.

### Examples
| Raw Dictation | Standard Mode (Risky) | Code Mode (Safe) |
|---------------|----------------------|------------------|
| "Import user controller dot ts from utils" | `Import user controller dot ts from utils` | `import { userController } from './utils'` |
| "Function get user async returns promise user" | `Function get user async returns promise user` | `async function getUser(): Promise<User>` |
| "Git commit dash m fix null pointer" | `Git commit dash m fix null pointer` | `git commit -m "fix null pointer"` |
| "Run npm install and then docker compose up dash d" | `Run npm install and then docker compose up dash d` | `npm install && docker compose up -d` |

## Integration
- Mode appears in the mode list as **Code**.
- Composable with **Personal Dictionary** to protect framework names (React, Next.js, Supabase, etc.).

## User Benefit
- Makes voice viable for "vibe coding" and quick refactors without breaking syntax.
- Reduces context-switching cost for developers who want to stay in the IDE.
