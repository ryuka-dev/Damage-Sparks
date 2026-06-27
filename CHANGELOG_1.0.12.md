# Damage Sparks 1.0.12

- Corrected the fallback TextMeshPro outline introduced in 1.0.11.
- Removed the enlarged black backing glyph, which changed glyph size and character spacing instead of creating a real border.
- Screen-space numbers now use four same-size black copies with small diagonal pixel offsets behind one colored face.
- World-space numbers use the same same-size offset approach in local billboard space.
- All outline copies remain pooled and reuse the renderer's shared font material.
- Keeps the renderer-change optimization and all 1.0.9 performance limits.
