## Procedure

### Objective

Locate the definition of a C# class while minimising unnecessary filesystem access and context usage.

### Strategy

1. Enumerate the project root to understand the high-level directory structure.
2. Search for files whose filename matches the requested class name (ignoring the file extension).
3. If a matching filename is found:

    * Read only that file.
    * Verify that it contains the requested class.
    * Return immediately if the class is found.
4. If no matching filename exists:

    * Enumerate the project's source directories.
    * Search candidate files for the requested class definition.
    * Inspect only files likely to contain the requested type.
5. Once the class has been located:

    * Return the full file path.
    * Return the namespace declaration.
    * Return the class declaration.
6. Stop searching immediately after a valid match has been confirmed unless the user explicitly requests all matching definitions.

### Guidelines

* Prefer filename searches before inspecting file contents.
* Never read an entire project when a targeted search can answer the request.
* Read the minimum amount of source code necessary to identify the requested class.
* Avoid repeatedly inspecting the same file.
* Minimise tool calls by progressively narrowing the search space.
* If multiple matching classes exist, present all candidates and allow the user to choose the desired one.
