# Space Sheep — Cline Project Rules

## Project

- Unity version: 6000.5.6f1
- Render Pipeline: URP 2D
- Input System: Unity Input System
- Project type: 2D
- Current main scene: `Assets/Scenes/SampleScene.unity`

## General behavior

- Before making changes, inspect the existing project structure and relevant files.
- Prefer modifying existing systems over creating duplicate systems.
- Do not delete, rename, or overwrite existing files or GameObjects unless explicitly requested.
- Do not change Project Settings, Package Manager dependencies, render pipeline settings, or Input System settings unless explicitly requested.
- Do not modify files inside `Library/`, `Temp/`, `Logs/`, or other Unity-generated folders.
- Keep changes focused on the current task.
- Avoid creating unnecessary files, folders, components, or abstractions.
- If a task is ambiguous, ask for clarification instead of guessing.

## Unity MCP

- Use Unity MCP when interacting with the Unity Editor or the currently open scene.
- Before modifying a scene, inspect the current scene hierarchy and relevant GameObjects.
- Prefer modifying existing GameObjects when appropriate instead of creating duplicates.
- Before creating a new GameObject, check whether an appropriate object already exists.
- Before adding a component, check whether the GameObject already has that component.
- Do not delete GameObjects without explicit permission.
- Do not modify unrelated objects in the scene.
- After making scene changes, verify that the intended objects and components exist.
- If an MCP operation fails, inspect the error and correct the operation rather than repeatedly performing the same action.

## C# conventions

- Use modern C# compatible with Unity 6000.5.6f1.
- Use clear, descriptive class, method, and variable names.
- Use PascalCase for classes and public members.
- Use camelCase for private fields and local variables.
- Prefer `[SerializeField] private` fields over public fields when Inspector exposure is needed.
- Keep MonoBehaviours focused on one responsibility.
- Avoid creating large "god" classes such as a single script containing all gameplay logic.
- Prefer composition and small reusable components.
- Do not introduce third-party libraries unless explicitly requested.
- Do not use deprecated Unity APIs when an appropriate current API exists.
- Use the Unity Input System rather than the legacy Input Manager.

## File organization

Use this structure when new folders are actually needed:

Assets/
    Scripts/
        Core/
        Player/
        Enemies/
        UI/
    Prefabs/
        Player/
        Enemies/
        Environment/
    Scenes/
    Art/
    Audio/

Do not create empty folders just to satisfy this structure.

## Scene organization

- Keep scene hierarchy readable.
- Use meaningful GameObject names.
- Avoid names such as `GameObject`, `GameObject (1)`, `New Game Object`, etc.
- Keep related objects grouped logically.
- Do not unnecessarily change transforms of existing objects.

## Workflow

For small tasks:

1. Inspect the relevant files or Unity objects.
2. Make the smallest necessary change.
3. Verify the result.
4. Report what was changed.

For larger tasks:

1. Inspect the project.
2. Explain the proposed implementation briefly.
3. Identify which files and/or Unity objects will be changed.
4. Ask for confirmation before making broad or destructive changes.
5. Implement the change.
6. Verify the result.
7. Report any remaining issues.

## Safety

Never:

- Delete project files without permission.
- Delete scene objects without permission.
- Modify Project Settings without permission.
- Modify packages or package versions without permission.
- Rewrite unrelated scripts.
- Replace an existing implementation simply because another approach is preferred.
- Make large architectural changes without discussing them first.

When uncertain, preserve the existing project and ask.