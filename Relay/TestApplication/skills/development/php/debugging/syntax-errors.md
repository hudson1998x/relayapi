---
title: Fixing PHP Syntax Errors
tags: php, debugging, syntax, error, parse
tools: php.syntax_check, filesystem.read, filesystem.patch
---

1. Extract the filename and line number from the error message.
2. Read the affected file, focusing on the reported line and surrounding context.
3. Identify the syntax issue — missing semicolon, unmatched brace, missing dollar sign, etc.
4. Apply the smallest valid fix.
5. Re-run the syntax checker to confirm the fix.
6. Report success or explain why the error persists.
