---
title: Locating a File or Directory
tags: filesystem, navigation, search, file, directory
tools: directory.list
---

1. Ask the user which directory to search in if not already specified.
2. Use directory.list to list the contents of the given directory.
3. If the target file or directory is found, return its full path immediately.
4. If not found, recursively list subdirectories one level at a time.
5. Return the first matching result or report that nothing was found.
