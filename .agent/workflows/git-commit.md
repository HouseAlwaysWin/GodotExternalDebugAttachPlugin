---
description: Git commit workflow - always ask user before committing
---

# Git Commit Workflow

## Rules
1. **Always ask user before committing** - Do not auto-commit or auto-push without explicit user approval
2. Stage changes with `git add .`
3. Show user what will be committed with `git status` 
4. Ask user to confirm before running `git commit` and `git push`
5. Wait for user's "好" or explicit approval before proceeding

## Example
```
我準備要提交以下變更：
- SettingsManager.cs: 新增 Cursor 支援
- README.md: 更新文件

是否可以 commit + push？
```
