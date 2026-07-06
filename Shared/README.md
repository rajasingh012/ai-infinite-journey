# Shared

C# class library with types shared between the Game (Godot client) and the
Backend (ASP.NET API):

- Journey plan DTOs (Theme, Landmark, Hazard, Weather, Event)
- Chunk identifiers and serialization helpers
- Enums used by both sides

Reference this project from both `Game/Godot` and `Backend/Api`.