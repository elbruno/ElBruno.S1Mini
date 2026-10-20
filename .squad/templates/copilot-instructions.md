# Copilot Coding Agent Instructions

This repository supports ordinary Copilot coding sessions as well as the optional **Squad** custom agent.

## Regular Coding Sessions

Work directly on the user's request using the current session's tools and worktree. Selecting Squad is not required, including in Copilot App when the custom agent is unavailable. Do not load the coordinator definition merely to satisfy a startup check or adopt its identity, routing, delegation, or state-management duties.

Follow applicable repository conventions and decisions in `.squad/decisions.md`, but team orchestration is not a prerequisite for regular coding work. Preserve existing Squad history and configuration unless the task calls for changing them.

After repository instruction changes, start a fresh session if the app is still using previously loaded instructions.

## Squad Issue Assignments

The remaining guidelines apply only when an issue is explicitly assigned through the Squad workflow or the user requests work as a Squad member. They do not turn a coding agent into the Squad coordinator. Coordinator-only startup and integrity checks belong in `.github/agents/squad.agent.md`, not in these shared instructions.

### Team Context

Before starting work on a Squad issue:

1. Read `.squad/team.md` for the team roster, member roles, and your capability profile.
2. Read `.squad/routing.md` for work routing rules.
3. If the issue has a `squad:{member}` label, read that member's charter at `.squad/agents/{member}/charter.md` to understand their domain expertise and coding style — work in their voice.

### Capability Self-Check

Before starting work, check your capability profile in `.squad/team.md` under the **Coding Agent → Capabilities** section.

- **🟢 Good fit** — proceed autonomously.
- **🟡 Needs review** — proceed, but note in the PR description that a squad member should review.
- **🔴 Not suitable** — do NOT start work. Instead, comment on the issue:
  ```
  🤖 This issue doesn't match my capability profile (reason: {why}). Suggesting reassignment to a squad member.
  ```

### Branch Naming

For Squad issue work, use the squad branch convention when creating a branch. If the app already provides a worktree branch, keep using it unless the user requests a rename.
```
squad/{issue-number}-{kebab-case-slug}
```
Example: `squad/42-fix-login-validation`

### PR Guidelines

When opening a PR:
- Reference the issue: `Closes #{issue-number}`
- If the issue had a `squad:{member}` label, mention the member: `Working as {member} ({role})`
- If this is a 🟡 needs-review task, add to the PR description: `⚠️ This task was flagged as "needs review" — please have a squad member review before merging.`
- Follow any project conventions in `.squad/decisions.md`

### Decisions

If you make a decision that affects other team members, write it to:
```
.squad/decisions/inbox/copilot-{brief-slug}.md
```
The Scribe will merge it into the shared decisions file.
