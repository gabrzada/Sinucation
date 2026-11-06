# Objetos menores do bar e lounge

As duas garrafas, o rádio, o cacto, a planta de chão e a luminária usam modelos próprios de `Assets/_Project/Resources/Models/FS/small-props.bytes`, gerados por `Blender/small_props.py`. As malhas têm as proporções dos FBX originais após normalização pela altura, preservando as posições e colliders existentes. Garrafas, rádio e cacto continuam sem collider; planta e luminária mantêm os colliders com a folga já usada no cenário.

A luminária não projeta sombra para não bloquear a luz que a sala coloca dentro da cúpula. A luz existente continua sendo controlada pelo Unity. Caso o asset não esteja disponível, `PropLibrary` recorre ao FBX original. As capturas em `../Logs/SmallPropsAfter/` mostram a montagem no editor.

Para reproduzir: `& 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' -b --factory-startup -P Blender/small_props.py`, inspecionar `Blender/small-props-refined/gallery.png` e copiar `Blender/small-props-refined/small-props.bytes` para `Assets/_Project/Resources/Models/FS/small-props.bytes`.
