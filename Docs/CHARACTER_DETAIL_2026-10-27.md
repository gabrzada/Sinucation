# Detalhamento dos personagens — 27/10/2026

O traje compartilhado recebeu bolsos discretos, insígnias nas mangas, acabamento nos punhos e detalhes nas botas. As proporções, os pivôs e as peças de mãos, cabeça e boné foram preservados. O asset de produção é `Assets/_Project/Resources/Models/FS/character.bytes`.

A fonte reproduzível é `Blender/character_detail_v2.py`; ela usa a cópia anterior em `Blender/revisions/2026-10-27-before-character-detail/character.bytes` e escreve os resultados em `Blender/character-detail-v2/`. O relatório `validation.json` registra os hashes e confirma que apenas `Torso`, `Boot` e `UpperArm` foram substituídos. Para restaurar o visual anterior, copie o arquivo da pasta de revisão para o caminho do asset de produção.

Verificação: captura das três formas e da pose de tacada no Unity; 59 testes EditMode de animação, cosméticos, transições e estilo passaram no projeto de teste isolado. Essas verificações não substituem uma revisão de todas as combinações e de uma partida multiplayer.

Os óculos ganharam hastes e dobradiças adaptadas aos três formatos de cabeça. A faixa do chapéu de festa foi afastada do cone para eliminar o serrilhado causado por superfícies coincidentes. A fonte do chapéu é `Blender/accessory_detail_v2.py`, e o asset anterior está em `Blender/revisions/2026-10-27-before-accessory-detail/accessories.bytes`; a exportação substitui somente o registro `PartyHat`.
